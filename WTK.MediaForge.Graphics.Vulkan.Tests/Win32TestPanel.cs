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

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
