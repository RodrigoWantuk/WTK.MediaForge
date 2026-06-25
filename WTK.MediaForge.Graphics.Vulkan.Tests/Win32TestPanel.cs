using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

internal static class Win32TestPanel
{
    private const int WsOverlappedWindow = 0x00CF0000;
    private static readonly string ClassName = $"WTKMediaForgeTestPanel_{Environment.ProcessId}";
    private static int _classRegistered;

    public static nint Create(int width = 640, int height = 360)
    {
        EnsureClassRegistered();

        var hwnd = CreateWindowEx(
            0,
            ClassName,
            string.Empty,
            WsOverlappedWindow,
            0,
            0,
            width,
            height,
            0,
            0,
            GetModuleHandle(null),
            0);

        if (hwnd == 0)
            throw new InvalidOperationException($"CreateWindowEx failed with error {Marshal.GetLastWin32Error()}.");

        return hwnd;
    }

    public static void Destroy(nint hwnd)
    {
        if (hwnd != 0)
            DestroyWindow(hwnd);
    }

    public static void ResizeClient(nint hwnd, int clientWidth, int clientHeight)
    {
        if (hwnd == 0)
            throw new ArgumentException("Window handle cannot be zero.", nameof(hwnd));

        if (clientWidth <= 0 || clientHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientWidth), "Client size must be positive.");

        var clientRect = new Rect
        {
            Right = clientWidth,
            Bottom = clientHeight
        };

        var style = GetWindowLong(hwnd, GwlStyle);
        if (!AdjustWindowRect(ref clientRect, style, false))
            throw new InvalidOperationException($"AdjustWindowRect failed with error {Marshal.GetLastWin32Error()}.");

        var windowWidth = clientRect.Right - clientRect.Left;
        var windowHeight = clientRect.Bottom - clientRect.Top;

        if (!SetWindowPos(hwnd, 0, 0, 0, windowWidth, windowHeight, SwpNoMove | SwpNoZOrder | SwpNoActivate))
            throw new InvalidOperationException($"SetWindowPos failed with error {Marshal.GetLastWin32Error()}.");
    }

    public static (int Width, int Height) GetClientSize(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var rect))
            throw new InvalidOperationException($"GetClientRect failed with error {Marshal.GetLastWin32Error()}.");

        return (rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private const int GwlStyle = -16;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static void EnsureClassRegistered()
    {
        if (Interlocked.Exchange(ref _classRegistered, 1) != 0)
            return;

        var windowClass = new WndClass
        {
            LpszClassName = ClassName,
            HInstance = GetModuleHandle(null),
            LpfnWndProc = DefWindowProc
        };

        if (RegisterClass(ref windowClass) == 0)
            throw new InvalidOperationException($"RegisterClass failed with error {Marshal.GetLastWin32Error()}.");
    }

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style;
        public WndProcDelegate LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        public string LpszMenuName;
        public string LpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass([In] ref WndClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AdjustWindowRect(ref Rect lpRect, int dwStyle, bool bMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
