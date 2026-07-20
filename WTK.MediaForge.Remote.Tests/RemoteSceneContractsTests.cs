using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Core.Identifiers;
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
}
