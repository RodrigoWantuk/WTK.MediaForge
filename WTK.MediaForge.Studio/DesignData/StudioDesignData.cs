using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.ViewModels;

namespace WTK.MediaForge.Studio.DesignData;

public static class StudioDesignData
{
    public static StudioShellViewModel CreateShellViewModel()
    {
        var shell = new StudioShellViewModel();

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
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Scene, "Main Scene", "1920 x 1080 / 60 fps", "SCN", "PROGRAM") { IsActive = true },
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Scene, "Interview", "Two camera layout", "SCN"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Scene, "Break BRB", "Holding screen", "SCN")
                }),
            new ProjectTreeGroupViewModel(
                "Sources",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Webcam", "Camera / 1080p60", "CAM", "LIVE"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Desktop Capture", "Display 1 / 1440p60", "DSP", "GPU"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Logo.png", "Image / 512 px", "IMG"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Lower Third", "Text template", "TXT"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Source, "Intro.mp4", "Media / buffered", "VID", "BUFFER")
                }),
            new ProjectTreeGroupViewModel(
                "Outputs",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Output, "Preview", "Local preview panel", "PRV", "RUNNING"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Output, "Recording MP4", "H.264 / 18 Mb/s", "REC", "READY"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Output, "RTMP Twitch", "6 Mb/s / Twitch", "RTM", "LIVE"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Output, "Virtual Camera", "Planned output", "VCM", "PLAN")
                }),
            new ProjectTreeGroupViewModel(
                "Presets",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Preset, "1080p Streaming", "16:9 / 60 fps", "PRE"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Preset, "YouTube 1080p60", "H.264 high profile", "PRE")
                }),
            new ProjectTreeGroupViewModel(
                "Packages",
                new[]
                {
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Package, "Starter Pack", "Scenes and source templates", "PKG"),
                    new ProjectTreeItemViewModel(StudioProjectItemKind.Package, "Brand Kit", "Lower thirds and logo set", "PKG", "v2")
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
