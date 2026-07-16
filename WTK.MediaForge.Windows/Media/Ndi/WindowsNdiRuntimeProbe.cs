using System.Runtime.InteropServices;

namespace WTK.MediaForge.Windows.Media.Ndi;

internal sealed record WindowsNdiRuntimeInfo(
    bool IsRuntimePresent,
    bool IsLoadable,
    string? LibraryPath,
    string? Version,
    string Reason)
{
    public bool CanUseStandardSdk =>
        IsRuntimePresent &&
        IsLoadable;

    public bool HasProductSafeGpuPath { get; init; }
}

internal interface IWindowsNdiRuntimeProbe
{
    WindowsNdiRuntimeInfo Probe();
}

internal sealed class WindowsNdiRuntimeProbe : IWindowsNdiRuntimeProbe
{
    private static readonly string[] RuntimeEnvironmentVariables =
    [
        "NDI_RUNTIME_DIR_V6",
        "NDI_RUNTIME_DIR_V5"
    ];

    private static readonly string[] WindowsLibraryNames =
    [
        "Processing.NDI.Lib.x64.dll",
        "Processing.NDI.Lib.x86.dll"
    ];

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, (bool Success, nint Handle, string? Version, string? Error)> _tryLoadLibrary;
    private readonly IReadOnlyList<string> _additionalSearchDirectories;

    public WindowsNdiRuntimeProbe(
        Func<string, string?>? getEnvironmentVariable = null,
        Func<string, bool>? fileExists = null,
        Func<string, (bool Success, nint Handle, string? Version, string? Error)>? tryLoadLibrary = null,
        IReadOnlyList<string>? additionalSearchDirectories = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _fileExists = fileExists ?? File.Exists;
        _tryLoadLibrary = tryLoadLibrary ?? TryLoadNativeLibrary;
        _additionalSearchDirectories = additionalSearchDirectories ?? [];
    }

    public WindowsNdiRuntimeInfo Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsNdiRuntimeInfo(
                IsRuntimePresent: false,
                IsLoadable: false,
                LibraryPath: null,
                Version: null,
                Reason: "NDI runtime probing is implemented in the Windows adapter only.");
        }

        var candidates = EnumerateCandidatePaths()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (!_fileExists(candidate))
                continue;

            var load = _tryLoadLibrary(candidate);
            if (!load.Success)
            {
                return new WindowsNdiRuntimeInfo(
                    IsRuntimePresent: true,
                    IsLoadable: false,
                    LibraryPath: candidate,
                    Version: null,
                    Reason: $"NDI runtime library was found but could not be loaded: {load.Error ?? "unknown loader error"}.");
            }

            if (load.Handle != 0)
                NativeLibrary.Free(load.Handle);

            return new WindowsNdiRuntimeInfo(
                IsRuntimePresent: true,
                IsLoadable: true,
                LibraryPath: candidate,
                Version: load.Version,
                Reason: "NDI runtime library is installed and loadable, but product NDI remains blocked until a GPU-safe input/output path is validated.");
        }

        return new WindowsNdiRuntimeInfo(
            IsRuntimePresent: false,
            IsLoadable: false,
            LibraryPath: null,
            Version: null,
            Reason: "NDI runtime library was not found. Install the NDI runtime/SDK and expose NDI_RUNTIME_DIR_V6 or NDI_RUNTIME_DIR_V5 to enable runtime probing.");
    }

    private IEnumerable<string> EnumerateCandidatePaths()
    {
        foreach (var directory in EnumerateSearchDirectories())
        {
            foreach (var libraryName in WindowsLibraryNames)
                yield return Path.Combine(directory, libraryName);
        }
    }

    private IEnumerable<string> EnumerateSearchDirectories()
    {
        foreach (var directory in _additionalSearchDirectories)
        {
            if (!string.IsNullOrWhiteSpace(directory))
                yield return directory;
        }

        foreach (var variable in RuntimeEnvironmentVariables)
        {
            var directory = _getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(directory))
                yield return directory!;
        }

        yield return AppContext.BaseDirectory;

        var path = _getEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return directory;
    }

    private static (bool Success, nint Handle, string? Version, string? Error) TryLoadNativeLibrary(string libraryPath)
    {
        try
        {
            if (!NativeLibrary.TryLoad(libraryPath, out var handle))
                return (false, 0, null, "NativeLibrary.TryLoad returned false.");

            string? version = null;
            if (NativeLibrary.TryGetExport(handle, "NDIlib_version", out var versionExport))
            {
                var versionDelegate = Marshal.GetDelegateForFunctionPointer<NdiVersionDelegate>(versionExport);
                var versionPointer = versionDelegate();
                version = Marshal.PtrToStringAnsi(versionPointer);
            }

            return (true, handle, version, null);
        }
        catch (Exception ex)
        {
            return (false, 0, null, ex.Message);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint NdiVersionDelegate();
}
