using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;
using WTK.MediaForge.Studio.DocumentModel;

namespace WTK.MediaForge.Studio.DesignData;

public static class StudioDesignData
{
    public static StudioShellViewModel CreateShellViewModel(StudioServiceBundle? services = null)
    {
        var document = StudioMockDocumentFactory.Create();
        var shell = services is null ? new StudioShellViewModel() : new StudioShellViewModel(services);

        shell.LoadDesignData(
            CreateProjectGroups(document),
            CreateLayers(document),
            CreateEffects(document),
            CreateDiagnostics(),
            CreatePerformanceMetrics(),
            CreateOutputs(document),
            CreateAudioStrips());

        return shell;
    }

    public static IReadOnlyList<ProjectTreeGroupViewModel> CreateProjectGroups()
    {
        return CreateProjectGroups(StudioMockDocumentFactory.Create());
    }

    public static IReadOnlyList<ProjectTreeGroupViewModel> CreateProjectGroups(StudioDocument document)
    {
        var scenes = document.Scenes
            .Select(scene => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Scene,
                scene.DisplayName,
                scene.Metadata,
                StudioIconKind.Scene,
                scene.IsProgram ? "PROGRAM" : string.Empty,
                id: scene.Id,
                typeId: "scene.canvas",
                detail: string.Join(", ", scene.OutputIds
                    .Select(id => document.Outputs.FirstOrDefault(output => output.Id == id)?.DisplayName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)))) { IsActive = scene.IsProgram })
            .ToArray();

        var sources = document.Sources
            .Select(source => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Source,
                source.DisplayName,
                source.Metadata,
                GetSourceIcon(source.TypeId),
                source.TypeId == "source.webcam" ? "LIVE" : source.TypeId == "source.desktop" ? "GPU" : source.Health == StudioHealthState.Warning ? "BUFFER" : string.Empty,
                source.Health,
                source.Id,
                source.TypeId,
                source.Endpoint))
            .ToArray();

        var outputs = document.Outputs
            .Select(output => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Output,
                output.DisplayName,
                output.TypeId == "output.preview" ? "Local preview panel" : $"{output.Codec} / {output.Bitrate}",
                GetOutputIcon(output.TypeId),
                output.State == StudioOutputState.Planned ? "PLAN" : output.State.ToString().ToUpperInvariant(),
                output.State == StudioOutputState.Planned ? StudioHealthState.Planned : StudioHealthState.Healthy,
                output.Id,
                output.TypeId,
                destination: output.Destination,
                codec: output.Codec,
                bitrate: output.Bitrate,
                secret: output.Secret))
            .ToArray();

        var presets = document.Presets
            .Select(preset => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Preset,
                preset.DisplayName,
                preset.Metadata,
                StudioIconKind.Preset,
                id: preset.Id,
                typeId: preset.TypeId))
            .ToArray();

        var packages = document.Packages
            .Select(pkg => new ProjectTreeItemViewModel(
                StudioProjectItemKind.Package,
                pkg.DisplayName,
                pkg.Metadata,
                StudioIconKind.Package,
                pkg.Id == "package-brand-kit" ? "v2" : string.Empty,
                id: pkg.Id,
                typeId: pkg.TypeId))
            .ToArray();

        return new[]
        {
            new ProjectTreeGroupViewModel("Scenes", scenes),
            new ProjectTreeGroupViewModel("Sources", sources),
            new ProjectTreeGroupViewModel("Outputs", outputs),
            new ProjectTreeGroupViewModel("Presets", presets),
            new ProjectTreeGroupViewModel("Packages", packages)
        };
    }

    public static IReadOnlyList<LayerItemViewModel> CreateLayers()
    {
        return CreateLayers(StudioMockDocumentFactory.Create());
    }

    public static IReadOnlyList<LayerItemViewModel> CreateLayers(StudioDocument document)
    {
        return document.Scenes
            .First(scene => scene.Id == document.SelectedSceneId)
            .Layers
            .OrderByDescending(layer => layer.Order)
            .Select(layer => new LayerItemViewModel(layer, GetLayerIcon(layer.Type, layer.SourceId)))
            .ToArray();
    }

    public static IReadOnlyList<EffectItemViewModel> CreateEffects()
    {
        return CreateEffects(StudioMockDocumentFactory.Create());
    }

    public static IReadOnlyList<EffectItemViewModel> CreateEffects(StudioDocument document)
    {
        var layer = document.Scenes
            .First(scene => scene.Id == document.SelectedSceneId)
            .Layers
            .First(item => item.Name == "Webcam");

        return layer.Effects.Select(effect => new EffectItemViewModel(effect)).ToArray();
    }

    public static IReadOnlyList<DiagnosticLogItemViewModel> CreateDiagnostics()
    {
        return new[]
        {
            new DiagnosticLogItemViewModel("12:16:01", "INFO", "Studio shell booted in UI mock mode."),
            new DiagnosticLogItemViewModel("12:16:03", "INFO", "Project package validation completed."),
            new DiagnosticLogItemViewModel("12:16:07", "WARN", "RTMP credentials are masked in the mock inspector."),
            new DiagnosticLogItemViewModel("12:16:12", "INFO", "Preview canvas overlay selected Lower Third."),
            new DiagnosticLogItemViewModel("12:16:19", "INFO", "Render graph preview is represented as design data only.")
        };
    }

    public static IReadOnlyList<PerformanceMetricViewModel> CreatePerformanceMetrics()
    {
        return new[]
        {
            new PerformanceMetricViewModel("Frame time", "16.6 ms", "P95 18.2 ms"),
            new PerformanceMetricViewModel("GPU", "31%", "Mock Vulkan queue"),
            new PerformanceMetricViewModel("VRAM", "642 MB", "Estimated surfaces"),
            new PerformanceMetricViewModel("Dropped", "0", "Backpressure clear")
        };
    }

    public static IReadOnlyList<OutputMonitorItemViewModel> CreateOutputs()
    {
        return CreateOutputs(StudioMockDocumentFactory.Create());
    }

    public static IReadOnlyList<OutputMonitorItemViewModel> CreateOutputs(StudioDocument document)
    {
        return document.Outputs
            .Select(output => new OutputMonitorItemViewModel(
                output.DisplayName,
                output.State,
                output.Destination,
                output.Bitrate,
                output.State == StudioOutputState.Planned ? "Planned" : "Healthy",
                output.TypeId))
            .ToArray();
    }

    public static IReadOnlyList<AudioStripViewModel> CreateAudioStrips()
    {
        return new[]
        {
            new AudioStripViewModel("Program", "-12 dB", false),
            new AudioStripViewModel("Mic 1", "-18 dB", false),
            new AudioStripViewModel("Desktop", "-24 dB", false),
            new AudioStripViewModel("Music", "-inf", true)
        };
    }

    private static StudioIconKind GetSourceIcon(string typeId)
    {
        return typeId switch
        {
            "source.webcam" => StudioIconKind.Camera,
            "source.desktop" => StudioIconKind.Desktop,
            "source.image" => StudioIconKind.Image,
            "source.text" => StudioIconKind.Text,
            "source.media" => StudioIconKind.Video,
            _ => StudioIconKind.Source
        };
    }

    private static StudioIconKind GetOutputIcon(string typeId)
    {
        return typeId switch
        {
            "output.preview" => StudioIconKind.Preview,
            "output.file.mp4" => StudioIconKind.Record,
            "output.rtmp" => StudioIconKind.Stream,
            _ => StudioIconKind.Output
        };
    }

    private static StudioIconKind GetLayerIcon(string layerType, string sourceId)
    {
        if (layerType == "Text")
        {
            return StudioIconKind.Text;
        }

        if (layerType == "Image")
        {
            return StudioIconKind.Image;
        }

        return sourceId.Contains("desktop", StringComparison.OrdinalIgnoreCase)
            ? StudioIconKind.Desktop
            : StudioIconKind.Camera;
    }
}
