using System.Runtime.InteropServices;

namespace WTK.MediaForge.Graphics.D3D11;

public static class Win32NativeHandle
{
    public static void CloseSharedHandle(nint handle)
    {
        if (handle == 0)
            return;

        if (!CloseHandle(handle))
            throw new InvalidOperationException($"CloseHandle failed with Win32 error {Marshal.GetLastWin32Error()}.");
    }

    public static nint DuplicateSharedHandle(nint handle)
    {
        if (handle == 0)
            throw new ArgumentOutOfRangeException(nameof(handle));

        const uint duplicateSameAccess = 0x00000002;

        if (!DuplicateHandle(
                GetCurrentProcess(),
                handle,
                GetCurrentProcess(),
                out nint duplicated,
                0,
                false,
                duplicateSameAccess))
        {
            throw new InvalidOperationException(
                $"DuplicateHandle failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        return duplicated;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        nint hSourceProcessHandle,
        nint hSourceHandle,
        nint hTargetProcessHandle,
        out nint lpTargetHandle,
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
