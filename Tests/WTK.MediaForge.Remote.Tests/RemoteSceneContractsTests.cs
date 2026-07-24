using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Remote;
using Xunit;

namespace WTK.MediaForge.Remote.Tests;

public sealed class RemoteSceneContractsTests
{
    [Fact]
    public void Publish_rejects_audio_until_the_audio_pipeline_exists()
    {
        var request = new RemoteScenePublishRequest("program", RenderOutputId.New(), EncodedVideoProfile.DefaultH264, IncludeAudio: true);
        Assert.Throws<NotSupportedException>(() => RemoteSceneRequestValidator.Validate(request));
    }

    [Fact]
    public void Connection_options_reject_insecure_signaling()
    {
        var options = new WebRtcConnectionOptions { SignalingServer = new Uri("http://example.test") };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Publisher_transfers_an_explicit_packet_lease_and_exposes_keyframe_feedback()
    {
        var send = typeof(IRemoteScenePublisher).GetMethod(nameof(IRemoteScenePublisher.SendVideoPacketAsync));

        Assert.NotNull(send);
        Assert.Equal(typeof(EncodedVideoPacketLease), send!.GetParameters()[0].ParameterType);
        Assert.NotNull(typeof(IRemoteScenePublisher).GetEvent(nameof(IRemoteScenePublisher.KeyFrameRequested)));
        Assert.Equal(
            typeof(IAsyncEnumerable<EncodedVideoPacketLease>),
            typeof(IRemoteSceneSubscriber).GetProperty(nameof(IRemoteSceneSubscriber.VideoPackets))!.PropertyType);
        Assert.Equal(
            typeof(RemoteSceneFormatChangedEventArgs),
            typeof(IRemoteSceneSubscriber).GetProperty(nameof(IRemoteSceneSubscriber.CurrentFormat))!.PropertyType);
        var format = new RemoteSceneFormatChangedEventArgs(1920, 1080, EncodedVideoProfile.DefaultH264, generation: 4);
        Assert.Equal(4, format.Generation);
    }

    [Fact]
    public void Packet_queue_policy_is_bounded_and_timed()
    {
        new RemoteScenePacketQueuePolicy().Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RemoteScenePacketQueuePolicy { Capacity = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RemoteScenePacketQueuePolicy { OperationTimeout = TimeSpan.Zero }.Validate());
    }

    [Fact]
    public void Canonical_remote_scene_settings_contain_policy_but_no_runtime_secrets()
    {
        var outputJson = RenderOutputSettingsSerializer.ToJson(new RemoteSceneOutputSettings
        {
            SignalingEndpoint = "wss://signal.example.test",
            StreamName = "program"
        }).ToJsonString();
        var sourceJson = MediaSourceSettingsSerializer.ToJson(new RemoteSceneSourceSettings
        {
            SignalingEndpoint = "wss://signal.example.test",
            StreamName = "guest"
        }).ToJsonString();

        Assert.Equal("remote-scene", RenderOutputTypes.RemoteScene.Value);
        Assert.Equal("remote-scene", MediaSourceTypes.RemoteScene.Value);
        Assert.Contains("reconnectAttempts", outputJson, StringComparison.Ordinal);
        Assert.Contains("preferredWidth", sourceJson, StringComparison.Ordinal);
        foreach (var secret in new[] { "accessToken", "inviteCode", "turnUsername", "turnCredential", "sessionToken" })
        {
            Assert.DoesNotContain(secret, outputJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, sourceJson, StringComparison.OrdinalIgnoreCase);
        }
    }
}
