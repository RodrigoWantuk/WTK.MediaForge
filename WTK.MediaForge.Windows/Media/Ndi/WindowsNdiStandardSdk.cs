using System.Runtime.InteropServices;

namespace WTK.MediaForge.Windows.Media.Ndi;

internal interface IWindowsNdiStandardSdk : IDisposable
{
    bool Initialize();

    IReadOnlyList<WindowsNdiSourceInfo> FindSources(
        WindowsNdiDiscoveryOptions options,
        CancellationToken cancellationToken);
}

internal interface IWindowsNdiStandardSdkFactory
{
    IWindowsNdiStandardSdk Load(string libraryPath);
}

internal sealed class WindowsNdiStandardSdkFactory : IWindowsNdiStandardSdkFactory
{
    public IWindowsNdiStandardSdk Load(string libraryPath) =>
        WindowsNdiStandardSdk.Load(libraryPath);
}

internal sealed class WindowsNdiStandardSdk : IWindowsNdiStandardSdk
{
    private static readonly object InitializationGate = new();
    private static int _initializationReferences;

    private readonly nint _libraryHandle;
    private readonly NdiInitializeDelegate _initialize;
    private readonly NdiDestroyDelegate _destroy;
    private readonly NdiFindCreateDelegate _findCreate;
    private readonly NdiFindDestroyDelegate _findDestroy;
    private readonly NdiFindWaitForSourcesDelegate _findWaitForSources;
    private readonly NdiFindGetCurrentSourcesDelegate _findGetCurrentSources;
    private bool _initialized;
    private bool _disposed;

    private WindowsNdiStandardSdk(
        nint libraryHandle,
        NdiInitializeDelegate initialize,
        NdiDestroyDelegate destroy,
        NdiFindCreateDelegate findCreate,
        NdiFindDestroyDelegate findDestroy,
        NdiFindWaitForSourcesDelegate findWaitForSources,
        NdiFindGetCurrentSourcesDelegate findGetCurrentSources)
    {
        _libraryHandle = libraryHandle;
        _initialize = initialize;
        _destroy = destroy;
        _findCreate = findCreate;
        _findDestroy = findDestroy;
        _findWaitForSources = findWaitForSources;
        _findGetCurrentSources = findGetCurrentSources;
    }

    public static WindowsNdiStandardSdk Load(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);

        if (!NativeLibrary.TryLoad(libraryPath, out var handle))
            throw new InvalidOperationException($"NDI runtime library could not be loaded from '{libraryPath}'.");

        try
        {
            return new WindowsNdiStandardSdk(
                handle,
                GetDelegate<NdiInitializeDelegate>(handle, "NDIlib_initialize"),
                GetDelegate<NdiDestroyDelegate>(handle, "NDIlib_destroy"),
                GetDelegate<NdiFindCreateDelegate>(handle, "NDIlib_find_create_v2"),
                GetDelegate<NdiFindDestroyDelegate>(handle, "NDIlib_find_destroy"),
                GetDelegate<NdiFindWaitForSourcesDelegate>(handle, "NDIlib_find_wait_for_sources"),
                GetDelegate<NdiFindGetCurrentSourcesDelegate>(handle, "NDIlib_find_get_current_sources"));
        }
        catch
        {
            NativeLibrary.Free(handle);
            throw;
        }
    }

    public bool Initialize()
    {
        ThrowIfDisposed();

        lock (InitializationGate)
        {
            if (_initializationReferences == 0 && !_initialize())
                return false;

            _initializationReferences++;
            _initialized = true;
            return true;
        }
    }

    public IReadOnlyList<WindowsNdiSourceInfo> FindSources(
        WindowsNdiDiscoveryOptions options,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_initialized)
            throw new InvalidOperationException("NDI Standard SDK must be initialized before source discovery.");

        cancellationToken.ThrowIfCancellationRequested();
        var groups = StringToCoTaskMemUtf8(options.Groups);
        var extraIps = StringToCoTaskMemUtf8(options.ExtraIps);
        nint finder = 0;

        try
        {
            var create = new NdiFindCreate
            {
                ShowLocalSources = options.ShowLocalSources ? (byte)1 : (byte)0,
                Groups = groups,
                ExtraIps = extraIps
            };

            finder = _findCreate(ref create);
            if (finder == 0)
                throw new InvalidOperationException("NDI source finder could not be created.");

            var timeoutMilliseconds = checked((uint)options.DiscoveryTimeout.TotalMilliseconds);
            if (timeoutMilliseconds > 0)
                _findWaitForSources(finder, timeoutMilliseconds);

            cancellationToken.ThrowIfCancellationRequested();
            var sourcesPointer = _findGetCurrentSources(finder, out var sourceCount);
            if (sourcesPointer == 0 || sourceCount == 0)
                return [];

            var count = checked((int)sourceCount);
            var result = new List<WindowsNdiSourceInfo>(count);
            var sourceSize = Marshal.SizeOf<NdiSource>();
            for (var i = 0; i < count; i++)
            {
                var sourcePointer = sourcesPointer + (i * sourceSize);
                var source = Marshal.PtrToStructure<NdiSource>(sourcePointer);
                var name = Marshal.PtrToStringUTF8(source.NdiName);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                result.Add(new WindowsNdiSourceInfo(
                    name!,
                    Marshal.PtrToStringUTF8(source.UrlAddress)));
            }

            return result;
        }
        finally
        {
            if (finder != 0)
                _findDestroy(finder);

            if (groups != 0)
                Marshal.FreeCoTaskMem(groups);

            if (extraIps != 0)
                Marshal.FreeCoTaskMem(extraIps);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_initialized)
        {
            lock (InitializationGate)
            {
                _initializationReferences = Math.Max(0, _initializationReferences - 1);
                if (_initializationReferences == 0)
                    _destroy();
            }
        }

        NativeLibrary.Free(_libraryHandle);
        _disposed = true;
    }

    private static T GetDelegate<T>(nint handle, string exportName)
        where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(handle, exportName, out var export))
            throw new MissingMethodException($"The NDI runtime library does not export '{exportName}'.");

        return Marshal.GetDelegateForFunctionPointer<T>(export);
    }

    private static nint StringToCoTaskMemUtf8(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? 0
            : Marshal.StringToCoTaskMemUTF8(value);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NdiFindCreate
    {
        public byte ShowLocalSources;
        public nint Groups;
        public nint ExtraIps;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NdiSource
    {
        public nint NdiName;
        public nint UrlAddress;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NdiInitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NdiDestroyDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NdiFindCreateDelegate(ref NdiFindCreate create);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NdiFindDestroyDelegate(nint finder);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool NdiFindWaitForSourcesDelegate(nint finder, uint timeoutMilliseconds);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NdiFindGetCurrentSourcesDelegate(nint finder, out uint sourceCount);
}
