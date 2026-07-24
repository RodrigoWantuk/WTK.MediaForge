using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WTK.MediaForge.Remote.WebRtc;
using Xunit;

namespace WTK.MediaForge.Remote.Tests;

public sealed class WebRtcNativeAbiTests
{
    [Fact]
    public void Managed_boundary_rejects_incompatible_or_unlinked_native_backend()
    {
        WebRtcNativeBridge.ValidateAbi(WebRtcNativeBridge.RequiredAbiVersion, backendAvailable: true);

        var incompatible = Assert.Throws<WebRtcNativeException>(() =>
            WebRtcNativeBridge.ValidateAbi(WebRtcNativeBridge.RequiredAbiVersion + 1, backendAvailable: true));
        Assert.Equal(WebRtcNativeResult.IncompatibleAbi, incompatible.Result);

        var unavailable = Assert.Throws<WebRtcNativeException>(() =>
            WebRtcNativeBridge.ValidateAbi(WebRtcNativeBridge.RequiredAbiVersion, backendAvailable: false));
        Assert.Equal(WebRtcNativeResult.BackendUnavailable, unavailable.Result);

        var native = Assert.Throws<WebRtcNativeException>(() =>
            WebRtcNativeBridge.ThrowIfFailed(WebRtcNativeResult.InvalidState, "native state failure"));
        Assert.Equal(WebRtcNativeResult.InvalidState, native.Result);
        Assert.Equal("native state failure", native.Message);
    }

    [Fact]
    public void Abi_header_contains_the_complete_versioned_encoded_packet_surface()
    {
        var root = FindRepositoryRoot();
        var header = File.ReadAllText(Path.Combine(
            root.FullName,
            "WTK.MediaForge.Remote.WebRtc.Native",
            "include",
            "wtk_mediaforge_webrtc.h"));

        foreach (var export in new[]
        {
            "mf_webrtc_session_create", "mf_webrtc_session_destroy",
            "mf_webrtc_session_create_offer", "mf_webrtc_session_set_local_description",
            "mf_webrtc_session_set_remote_description", "mf_webrtc_session_add_ice_candidate",
            "mf_webrtc_session_add_ice_server", "mf_webrtc_session_connect",
            "mf_webrtc_session_close", "mf_webrtc_publisher_send_h264",
            "mf_webrtc_publisher_send_audio", "mf_webrtc_session_set_video_packet_callback",
            "mf_webrtc_session_set_audio_packet_callback",
            "mf_webrtc_session_set_keyframe_request_callback",
            "mf_webrtc_session_set_state_callback",
            "mf_webrtc_session_set_ice_candidate_callback",
            "mf_webrtc_session_get_selected_candidate", "mf_webrtc_session_get_stats"
        })
            Assert.Contains(export, header, StringComparison.Ordinal);

        Assert.Contains("struct_size", header, StringComparison.Ordinal);
        Assert.Contains("struct_version", header, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoFrame", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Supply_chain_manifest_pins_source_toolchain_constraints_and_wrapper_hashes()
    {
        var root = FindRepositoryRoot();
        var native = Path.Combine(root.FullName, "WTK.MediaForge.Remote.WebRtc.Native");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(native, "native-supply-chain.json")));
        var source = document.RootElement.GetProperty("source");

        Assert.Equal("86676d3c4ee49b92380647d4b68388ed8f0ce94a", source.GetProperty("revision").GetString());
        Assert.Equal("34010c8b649b5938784d015318dc100ce80c3285", source.GetProperty("gitTree").GetString());
        Assert.False(document.RootElement.GetProperty("mediaBoundary").GetProperty("rawVideoFrames").GetBoolean());
        Assert.False(document.RootElement.GetProperty("mediaBoundary").GetProperty("softwareCodecs").GetBoolean());

        foreach (var wrapper in document.RootElement.GetProperty("wrapperFiles").EnumerateArray())
        {
            var path = Path.Combine(native, wrapper.GetProperty("path").GetString()!);
            var actual = ComputeCanonicalTextSha256(path);
            Assert.Equal(wrapper.GetProperty("sha256").GetString(), actual);
        }
    }

    [Fact]
    public void Missing_native_library_is_reported_without_promoting_capability()
    {
        var available = WebRtcNativeBridge.IsAvailable(out var reason);
        if (!available)
            Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    private static string ComputeCanonicalTextSha256(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var text = utf8.GetString(bytes);

        if (text.Length > 0 && text[0] == '\uFEFF')
            throw new InvalidDataException($"Wrapper file '{path}' must be UTF-8 without BOM.");

        var canonical = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "WTK.MediaForge.sln")))
            current = current.Parent;
        return current ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
