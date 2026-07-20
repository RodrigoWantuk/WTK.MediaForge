using System.Runtime.InteropServices;

namespace WTK.MediaForge.Remote.WebRtc;

/// <summary>Managed ABI boundary. The native bridge accepts encoded H.264 access units only.</summary>
public static class WebRtcNativeBridge
{
    public const int RequiredAbiVersion = 1;

    public static bool IsAvailable(out string reason)
    {
        if (!NativeLibrary.TryLoad("wtk_mediaforge_webrtc", out var library))
        {
            reason = "The pinned libwebrtc native bridge is not installed for this runtime.";
            return false;
        }

        try
        {
            var version = mf_webrtc_abi_version();
            reason = version == RequiredAbiVersion
                ? string.Empty
                : $"WebRTC native bridge ABI {version} is incompatible with required ABI {RequiredAbiVersion}.";
            return version == RequiredAbiVersion;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [DllImport("wtk_mediaforge_webrtc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int mf_webrtc_abi_version();
}
