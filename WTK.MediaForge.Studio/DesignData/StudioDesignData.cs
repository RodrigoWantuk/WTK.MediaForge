using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.DesignData;

public static class StudioDesignData
{
    public static StudioShellViewModel CreateShellViewModel(StudioServiceBundle? services = null)
    {
        var shell = services is null ? new StudioShellViewModel() : new StudioShellViewModel(services);

        shell.LoadDesignData(
            CreateProjectGroups(),
            CreateLayers(),
            CreateEffects(),
            CreateDiagnostics(),
            CreatePerformanceMetrics(),
            CreateOutputs(),
            CreateAudioStrips());

        return shell;
    }

    public static IReadOnlyList<ProjectTreeGroupViewModel> CreateProjectGroups()
    {
        return new[]
        {
            new ProjectTreeGroupViewModel(
                "Scenes",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Scene, "Main Scene", "1920 x 1080 / 60 fps", "SCN", "PROGRAM", id: "scene-main", typeId: "scene.canvas", detail: "Preview, Recording MP4, RTMP Twitch") { IsActive = true },
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Scene, "Interview", "Two camera layout", "SCN", id: "scene-interview", typeId: "scene.canvas", detail: "Preview"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Scene, "Break BRB", "Holding screen", "SCN", id: "scene-brb", typeId: "scene.canvas", detail: "Preview")
                }),
            new ProjectTreeGroupViewModel(
                "Sources",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Webcam", "Camera / 1080p60", "CAM", "LIVE", id: "source-webcam", typeId: "source.webcam", detail: "Logitech BRIO / Device 0"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Desktop Capture", "Display 1 / 1440p60", "DSP", "GPU", id: "source-desktop-1", typeId: "source.desktop", detail: "Display 1 / Desktop duplication"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Logo.png", "Image / 512 px", "IMG", id: "source-logo", typeId: "source.image", detail: "assets/brand/Logo.png"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Lower Third", "Text template", "TXT", id: "source-lower-third", typeId: "source.text", detail: "Text template / Brand Kit"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Intro.mp4", "Media / buffered", "VID", "BUFFER", StudioHealthState.Warning, id: "source-intro", typeId: "source.media", detail: "media/intro.mp4")
                }),
            new ProjectTreeGroupViewModel(
                "Outputs",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Output, "Preview", "Local preview panel", "PRV", "RUNNING", id: "output-preview", typeId: "output.preview", destination: "Local preview panel", codec: "RGBA", bitrate: "GPU surface"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Output, "Recording MP4", "H.264 / 18 Mb/s", "REC", "READY", id: "output-recording", typeId: "output.file.mp4", destination: "D:/captures/session.mp4", codec: "H.264", bitrate: "18 Mb/s"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Output, "RTMP Twitch", "6 Mb/s / Twitch", "RTM", "LIVE", id: "output-rtmp-twitch", typeId: "output.rtmp", destination: "rtmp://live.twitch.tv/app", codec: "H.264", bitrate: "6 Mb/s", secret: "sk_live_2d97c8a6_raw_secret"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Output, "Virtual Camera", "Planned output", "VCM", "PLAN", StudioHealthState.Planned, id: "output-virtual-camera", typeId: "output.virtual-camera", destination: "Virtual camera device", codec: "NV12", bitrate: "60 fps")
                }),
            new ProjectTreeGroupViewModel(
                "Presets",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Preset, "1080p Streaming", "16:9 / 60 fps", "PRE", id: "preset-1080p-streaming", typeId: "preset.canvas-output"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Preset, "YouTube 1080p60", "H.264 high profile", "PRE", id: "preset-youtube-1080p60", typeId: "preset.output")
                }),
            new ProjectTreeGroupViewModel(
                "Packages",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Package, "Starter Pack", "Scenes and source templates", "PKG", id: "package-starter", typeId: "package.scene"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Package, "Brand Kit", "Lower thirds and logo set", "PKG", "v2", id: "package-brand-kit", typeId: "package.brand")
                })
        };
    }

    public static IReadOnlyList<LayerItemViewModel> CreateLayers()
    {
        return new[]
        {
            new LayerItemViewModel("Lower Third", "Lower Third", "Text", "TXT", 4),
            new LayerItemViewModel("Logo.png", "Logo.png", "Image", "IMG", 3),
            new LayerItemViewModel("Webcam", "Webcam", "Source", "CAM", 2),
            new LayerItemViewModel("Desktop Capture", "Desktop Capture", "Source", "DSP", 1)
        };
    }

    public static IReadOnlyList<EffectItemViewModel> CreateEffects()
    {
        return new[]
        {
            new EffectItemViewModel("Chroma Key", "Key color #24ff71, tolerance 0.32, spill 0.18", true, true),
            new EffectItemViewModel("Blur", "Gaussian blur placeholder, disabled", false, false),
            new EffectItemViewModel("Color Correction", "Lift/gamma/gain placeholder", false, false)
        };
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
        return new[]
        {
            new OutputMonitorItemViewModel("Preview", StudioOutputState.Running, "Panel A", "GPU surface", "Healthy"),
            new OutputMonitorItemViewModel("Recording MP4", StudioOutputState.Recording, "D:/captures/session.mp4", "18 Mb/s", "Writing"),
            new OutputMonitorItemViewModel("RTMP Twitch", StudioOutputState.Live, "rtmp://live.twitch.tv/app", "6 Mb/s", "Stable"),
            new OutputMonitorItemViewModel("Virtual Camera", StudioOutputState.Planned, "Device output", "60 fps", "Planned")
        };
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
}
