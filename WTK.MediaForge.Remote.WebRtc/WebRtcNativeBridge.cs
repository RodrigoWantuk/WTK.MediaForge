using System.Runtime.InteropServices;

namespace WTK.MediaForge.Remote.WebRtc;

public enum WebRtcNativeResult
{
    Ok = 0,
    InvalidArgument = 1,
    IncompatibleAbi = 2,
    InvalidState = 3,
    BackendUnavailable = 4,
    OperationFailed = 5,
    BufferTooSmall = 6
}

public sealed class WebRtcNativeException : Exception
{
    public WebRtcNativeException(WebRtcNativeResult result, string message)
        : base(string.IsNullOrWhiteSpace(message) ? $"Native WebRTC operation failed with {result}." : message)
    {
        Result = result;
    }

    public WebRtcNativeResult Result { get; }
}

/// <summary>
/// Managed ABI boundary for the pinned encoded-access-unit libwebrtc bridge.
/// Availability requires both ABI compatibility and a linked libwebrtc backend.
/// </summary>
public static class WebRtcNativeBridge
{
    public const uint RequiredAbiVersion = 2;
    public const string LibraryName = "wtk_mediaforge_webrtc";

    public static bool IsAvailable(out string reason)
    {
        if (!NativeLibrary.TryLoad(LibraryName, out var library))
        {
            reason = "The pinned libwebrtc native bridge is not installed for this runtime.";
            return false;
        }

        try
        {
            var abi = GetDelegate<AbiVersionDelegate>(library, "mf_webrtc_abi_version")();
            if (abi != RequiredAbiVersion)
            {
                reason = $"WebRTC native bridge ABI {abi} is incompatible with required ABI {RequiredAbiVersion}.";
                return false;
            }

            var backendAvailable = GetDelegate<BackendAvailableDelegate>(
                library,
                "mf_webrtc_backend_available")() != 0;
            reason = backendAvailable
                ? string.Empty
                : "The ABI library is present, but the pinned libwebrtc backend is not linked.";
            return backendAvailable;
        }
        catch (Exception exception) when (
            exception is EntryPointNotFoundException or
            BadImageFormatException or
            MarshalDirectiveException)
        {
            reason = $"The native WebRTC bridge could not be validated: {exception.Message}";
            return false;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    public static void ValidateAbi(uint actualVersion, bool backendAvailable)
    {
        if (actualVersion != RequiredAbiVersion)
            throw new WebRtcNativeException(
                WebRtcNativeResult.IncompatibleAbi,
                $"WebRTC native bridge ABI {actualVersion} is incompatible with required ABI {RequiredAbiVersion}.");
        if (!backendAvailable)
            throw new WebRtcNativeException(
                WebRtcNativeResult.BackendUnavailable,
                "The ABI library is present, but the pinned libwebrtc backend is not linked.");
    }

    public static void ThrowIfFailed(WebRtcNativeResult result, string? nativeMessage)
    {
        if (result != WebRtcNativeResult.Ok)
            throw new WebRtcNativeException(result, nativeMessage ?? string.Empty);
    }

    private static T GetDelegate<T>(nint library, string export) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(library, export, out var address))
            throw new EntryPointNotFoundException($"Native WebRTC export '{export}' is missing.");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte BackendAvailableDelegate();
}
