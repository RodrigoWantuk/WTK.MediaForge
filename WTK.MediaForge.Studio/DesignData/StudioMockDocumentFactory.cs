using WTK.MediaForge.Studio.DocumentModel;
using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.DesignData;

public static class StudioMockDocumentFactory
{
    public static StudioDocument Create()
    {
        var document = new StudioDocument
        {
            Id = "studio-doc-live-production",
            DisplayName = "Live Production Workspace",
            SelectedSceneId = "scene-main"
        };

        SeedSources(document);
        SeedOutputs(document);
        SeedScenes(document);
        SeedPresets(document);
        SeedPackages(document);

        return document;
    }

    private static void SeedSources(StudioDocument document)
    {
        document.Sources.Add(new StudioSource
        {
            Id = "source-webcam",
            DisplayName = "Webcam",
            TypeId = "source.webcam",
            Metadata = "Camera / 1080p60",
            Endpoint = "Logitech BRIO / Device 0"
        });
        document.Sources.Add(new StudioSource
        {
            Id = "source-desktop-1",
            DisplayName = "Desktop Capture",
            TypeId = "source.desktop",
            Metadata = "Display 1 / 1440p60",
            Endpoint = "Display 1"
        });
        document.Sources.Add(new StudioSource
        {
            Id = "source-logo",
            DisplayName = "Logo.png",
            TypeId = "source.image",
            Metadata = "Image / 512 px",
            Endpoint = "assets/brand/Logo.png"
        });
        document.Sources.Add(new StudioSource
        {
            Id = "source-lower-third",
            DisplayName = "Lower Third",
            TypeId = "source.text",
            Metadata = "Text template",
            Endpoint = "Brand Kit / lower-third"
        });
        document.Sources.Add(new StudioSource
        {
            Id = "source-intro",
            DisplayName = "Intro.mp4",
            TypeId = "source.media",
            Metadata = "Media / buffered",
            Endpoint = "media/intro.mp4",
            Health = StudioHealthState.Warning
        });
    }

    private static void SeedOutputs(StudioDocument document)
    {
        document.Outputs.Add(new StudioOutput
        {
            Id = "output-preview",
            DisplayName = "Preview",
            TypeId = "output.preview",
            Destination = "Panel A",
            Codec = "RGBA",
            Bitrate = "GPU surface",
            AssignedSceneId = "scene-main",
            State = StudioOutputState.Running
        });
        document.Outputs.Add(new StudioOutput
        {
            Id = "output-recording",
            DisplayName = "Recording MP4",
            TypeId = "output.file.mp4",
            Destination = "D:/captures/session.mp4",
            Codec = "H.264",
            Bitrate = "18 Mb/s",
            AssignedSceneId = "scene-main",
            State = StudioOutputState.Recording
        });
        document.Outputs.Add(new StudioOutput
        {
            Id = "output-rtmp-twitch",
            DisplayName = "RTMP Twitch",
            TypeId = "output.rtmp",
            Destination = "rtmp://live.twitch.tv/app",
            Codec = "H.264",
            Bitrate = "6 Mb/s",
            Secret = "sk_live_2d97c8a6_raw_secret",
            AssignedSceneId = "scene-main",
            State = StudioOutputState.Live
        });
        document.Outputs.Add(new StudioOutput
        {
            Id = "output-virtual-camera",
            DisplayName = "Virtual Camera",
            TypeId = "output.virtual-camera",
            Destination = "Virtual camera device",
            Codec = "NV12",
            Bitrate = "60 fps",
            AssignedSceneId = "scene-interview",
            IsConfigured = false,
            State = StudioOutputState.Planned
        });
    }

    private static void SeedScenes(StudioDocument document)
    {
        var main = new StudioScene
        {
            Id = "scene-main",
            DisplayName = "Main Scene",
            Metadata = "1920 x 1080 / 60 fps",
            IsProgram = true
        };
        main.OutputIds.Add("output-preview");
        main.OutputIds.Add("output-recording");
        main.OutputIds.Add("output-rtmp-twitch");
        main.Layers.Add(CreateLayer("layer-desktop", "Desktop Capture", "source-desktop-1", "Desktop Capture", "Source", 1, 54, 46, 1812, 860, 100));
        main.Layers.Add(CreateLayer("layer-webcam", "Webcam", "source-webcam", "Webcam", "Source", 2, 1378, 106, 410, 232, 100));
        main.Layers.Add(CreateLayer("layer-logo", "Logo.png", "source-logo", "Logo.png", "Image", 3, 1664, 926, 176, 104, 96));
        main.Layers.Add(CreateLayer("layer-lower-third", "Lower Third", "source-lower-third", "Lower Third", "Text", 4, 192, 820, 1280, 148, 92));
        document.Scenes.Add(main);

        var interview = new StudioScene
        {
            Id = "scene-interview",
            DisplayName = "Interview",
            Metadata = "Two camera layout"
        };
        interview.OutputIds.Add("output-virtual-camera");
        interview.Layers.Add(CreateLayer("layer-interview-desktop", "Desktop Capture", "source-desktop-1", "Desktop Capture", "Source", 1, 80, 82, 1120, 630, 100));
        interview.Layers.Add(CreateLayer("layer-interview-webcam", "Webcam", "source-webcam", "Webcam", "Source", 2, 1230, 102, 560, 315, 100));
        interview.Layers.Add(CreateLayer("layer-interview-logo", "Logo.png", "source-logo", "Logo.png", "Image", 3, 1540, 870, 220, 130, 92));
        document.Scenes.Add(interview);

        var brb = new StudioScene
        {
            Id = "scene-brb",
            DisplayName = "Break BRB",
            Metadata = "Holding screen"
        };
        brb.Layers.Add(CreateLayer("layer-brb-background", "Intro.mp4", "source-intro", "Intro.mp4", "Source", 1, 0, 0, 1920, 1080, 100));
        brb.Layers.Add(CreateLayer("layer-brb-logo", "Logo.png", "source-logo", "Logo.png", "Image", 2, 760, 280, 400, 240, 100));
        brb.Layers.Add(CreateLayer("layer-brb-text", "Lower Third", "source-lower-third", "Lower Third", "Text", 3, 520, 660, 880, 160, 95));
        document.Scenes.Add(brb);
    }

    private static StudioLayer CreateLayer(
        string id,
        string name,
        string sourceId,
        string sourceName,
        string type,
        int order,
        double x,
        double y,
        double width,
        double height,
        double opacity)
    {
        var layer = new StudioLayer
        {
            Id = id,
            Name = name,
            SourceId = sourceId,
            SourceName = sourceName,
            Type = type,
            Order = order
        };
        layer.Transform.X = x;
        layer.Transform.Y = y;
        layer.Transform.Width = width;
        layer.Transform.Height = height;
        layer.Transform.Opacity = opacity;
        layer.Effects.Add(new StudioEffect
        {
            Id = $"{id}-effect-chroma",
            Name = "Chroma Key",
            Description = "Green spill tightened, edge smooth 0.24",
            IsEnabled = name == "Webcam",
            IsExpanded = name == "Webcam"
        });
        layer.Effects.Add(new StudioEffect
        {
            Id = $"{id}-effect-blur",
            Name = "Blur",
            Description = "Soft blur reserved for this layer",
            IsEnabled = false,
            IsExpanded = false,
            Tolerance = 0.12
        });
        return layer;
    }

    private static void SeedPresets(StudioDocument document)
    {
        document.Presets.Add(new StudioPreset
        {
            Id = "preset-1080p-streaming",
            DisplayName = "1080p Streaming",
            Metadata = "16:9 / 60 fps",
            TypeId = "preset.canvas-output"
        });
        document.Presets.Add(new StudioPreset
        {
            Id = "preset-youtube-1080p60",
            DisplayName = "YouTube 1080p60",
            Metadata = "H.264 high profile",
            TypeId = "preset.output"
        });
    }

    private static void SeedPackages(StudioDocument document)
    {
        document.Packages.Add(new StudioPackage
        {
            Id = "package-starter",
            DisplayName = "Starter Pack",
            Metadata = "Scenes and source templates",
            TypeId = "package.scene"
        });
        document.Packages.Add(new StudioPackage
        {
            Id = "package-brand-kit",
            DisplayName = "Brand Kit",
            Metadata = "Lower thirds and logo set",
            TypeId = "package.brand"
        });
    }
}
