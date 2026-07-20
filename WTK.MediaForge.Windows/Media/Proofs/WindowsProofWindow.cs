using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WTK.MediaForge.Windows.Media.Proofs;

internal sealed partial class WindowsProofWindow : IAsyncDisposable
{
    private static readonly TimeSpan LifecycleTimeout = TimeSpan.FromSeconds(5);
    private readonly TaskCompletionSource<nint> _handleReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _threadCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private uint _threadId;
    private nint _windowHandle;
    private int _disposed;

    private WindowsProofWindow()
    {
        _thread = new Thread(WindowThreadMain)
        {
            IsBackground = true,
            Name = "MediaForge-WGC-ProofWindow"
        };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    public nint Handle =>
        _windowHandle != 0
            ? _windowHandle
            : throw new InvalidOperationException("Proof window has not been created.");

    public static async ValueTask<WindowsProofWindow> CreateAsync(CancellationToken cancellationToken)
    {
        var window = new WindowsProofWindow();
        window._thread.Start();
        try
        {
            window._windowHandle = await window._handleReady.Task
                .WaitAsync(LifecycleTimeout, cancellationToken)
                .ConfigureAwait(false);
            return window;
        }
        catch
        {
            await window.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var handle = Volatile.Read(ref _windowHandle);
        if (handle != 0)
            PostMessage(handle, WindowMessageClose, 0, 0);
        var threadId = Volatile.Read(ref _threadId);
        if (threadId != 0)
            PostThreadMessage(threadId, WindowMessageQuit, 0, 0);

        await _threadCompleted.Task
            .WaitAsync(LifecycleTimeout, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private void WindowThreadMain()
    {
        nint handle = 0;
        try
        {
            Volatile.Write(ref _threadId, GetCurrentThreadId());
            handle = CreateWindowEx(
                0,
                "STATIC",
                "WTK MediaForge Window Capture Proof",
                WindowStyleOverlappedWindow | WindowStyleVisible,
                UseDefault,
                UseDefault,
                640,
                360,
                0,
                0,
                0,
                0);
            if (handle == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create WGC proof window.");

            Volatile.Write(ref _windowHandle, handle);
            ShowWindow(handle, ShowNormal);
            UpdateWindow(handle);
            _handleReady.TrySetResult(handle);

            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(in message);
                DispatchMessage(in message);
            }
        }
        catch (Exception ex)
        {
            _handleReady.TrySetException(ex);
        }
        finally
        {
            if (handle != 0 && IsWindow(handle))
                DestroyWindow(handle);
            Volatile.Write(ref _windowHandle, 0);
            _threadCompleted.TrySetResult();
        }
    }

    private const uint WindowStyleOverlappedWindow = 0x00CF0000;
    private const uint WindowStyleVisible = 0x10000000;
    private const uint WindowMessageClose = 0x0010;
    private const uint WindowMessageQuit = 0x0012;
    private const int ShowNormal = 1;
    private const int UseDefault = unchecked((int)0x80000000);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial nint DispatchMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static partial uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
