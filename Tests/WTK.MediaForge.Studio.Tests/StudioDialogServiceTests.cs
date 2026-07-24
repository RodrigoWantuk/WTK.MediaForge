using WTK.MediaForge.Studio.DesignData;
using WTK.MediaForge.Studio.Services;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class StudioDialogServiceTests
{
    [Fact]
    public void Add_source_request_is_capability_driven_and_scene_contextual()
    {
        var document = StudioMockDocumentFactory.Create();
        var scene = document.Scenes.Single(item => item.Id == "scene-main");
        var service = CreateService();

        var request = service.CreateAddSourceRequest(document, scene);

        Assert.Equal("source-library", request.Kind);
        Assert.Equal("Adicionar fonte", request.Title);
        Assert.Contains(scene.DisplayName, request.Message, StringComparison.Ordinal);
        Assert.Contains(request.Options, option => option.Id == "source.image" && option.IsEnabled && option.Badge == "Suportado");
        Assert.Contains(request.Options, option => option.Id == "source.webcam" && !option.IsEnabled && option.Badge == "Indisponível");
        Assert.Contains(request.Options, option => option.Id == "source.ndi" && !option.IsEnabled && option.Badge == "Bloqueado");
    }

    [Fact]
    public void Configure_output_request_links_capabilities_to_existing_outputs()
    {
        var document = StudioMockDocumentFactory.Create();
        var service = CreateService();

        var request = service.CreateConfigureOutputRequest(document);

        Assert.Equal("output-library", request.Kind);
        Assert.Contains(request.Options, option =>
            option.Id == "output.preview" &&
            option.IsEnabled &&
            option.Description.Contains("Atual:", StringComparison.Ordinal));
        Assert.Contains(request.Options, option =>
            option.Id == "output.file.mp4" &&
            !option.IsEnabled &&
            option.Description.Contains("hardware encode", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(request.Options, option =>
            option.Id == "output.virtual-camera" &&
            !option.IsEnabled &&
            option.Badge == "Planejado");
    }

    [Fact]
    public void Route_output_request_uses_output_live_state_transition_and_current_scene()
    {
        var document = StudioMockDocumentFactory.Create();
        var output = document.Outputs.Single(item => item.Id == "output-rtmp-twitch");
        output.IsLive = true;
        var service = CreateService();

        var request = service.CreateRouteOutputRequest(document, output.Id, "scene-brb");

        Assert.Equal("route-output", request.Kind);
        Assert.Equal("Transicionar", request.PrimaryText);
        Assert.True(request.RequiresLiveConfirmation);
        Assert.Equal(output.Id, request.TargetOutputId);
        Assert.Equal("scene-brb", request.SelectedSceneId);
        Assert.Equal(output.DefaultTransitionId, request.SelectedTransitionId);
        Assert.Contains(request.TransitionOptions, transition => transition.Id == "transition-fade");
        Assert.Contains(request.Options, option => option.Id == "scene-main" && option.IsEnabled);
    }

    [Fact]
    public void Route_output_request_uses_alterar_for_non_live_outputs()
    {
        var document = StudioMockDocumentFactory.Create();
        var output = document.Outputs.Single(item => item.Id == "output-rtmp-twitch");
        output.IsLive = false;
        output.State = WTK.MediaForge.Studio.Models.StudioOutputState.Running;
        var service = CreateService();

        var request = service.CreateRouteOutputRequest(document, output.Id, null);

        Assert.Equal("Alterar", request.PrimaryText);
        Assert.False(request.RequiresLiveConfirmation);
        Assert.Equal(output.AssignedSceneId, request.SelectedSceneId);
    }

    private static StudioDialogService CreateService() =>
        new(new FakeStudioCapabilityService());
}
