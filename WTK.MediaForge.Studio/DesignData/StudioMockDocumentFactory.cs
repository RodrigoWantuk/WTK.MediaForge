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
            DisplayName = "Produção ao vivo",
            SelectedSceneId = "scene-main"
        };

        SeedTransitions(document);
        SeedSources(document);
        SeedOutputs(document);
        SeedScenes(document);
        SeedPresets(document);
        SeedPackages(document);

        return document;
    }

    private static void SeedTransitions(StudioDocument document)
    {
        document.Transitions.Add(new StudioTransition
        {
            Id = "transition-cut",
            DisplayName = "Corte rápido",
            Kind = StudioTransitionKind.Cut,
            DurationMs = 0
        });
        document.Transitions.Add(new StudioTransition
        {
            Id = "transition-fade",
            DisplayName = "Fade",
            Kind = StudioTransitionKind.Fade,
            DurationMs = 300
        });
        document.Transitions.Add(new StudioTransition
        {
            Id = "transition-dissolve",
            DisplayName = "Dissolver",
            Kind = StudioTransitionKind.Dissolve,
            DurationMs = 450
        });
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
            DisplayName = "Captura de tela",
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
            DisplayName = "Tarja inferior",
            TypeId = "source.text",
            Metadata = "Modelo de texto",
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
            DisplayName = "Prévia local",
            TypeId = "output.preview",
            Destination = "Painel A",
            Codec = "RGBA",
            Bitrate = "Superfície local",
            AssignedSceneId = "scene-main",
            DefaultTransitionId = "transition-cut",
            TransitionDurationMs = 0,
            State = StudioOutputState.Running
        });
        document.Outputs.Add(new StudioOutput
        {
            Id = "output-recording",
            DisplayName = "Gravação MP4",
            TypeId = "output.file.mp4",
            Destination = "D:/captures/session.mp4",
            Codec = "H.264",
            Bitrate = "18 Mb/s",
            AssignedSceneId = "scene-main",
            DefaultTransitionId = "transition-fade",
            TransitionDurationMs = 300,
            IsRecording = true,
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
            DefaultTransitionId = "transition-fade",
            TransitionDurationMs = 300,
            IsLive = true,
            State = StudioOutputState.Live
        });
        document.Outputs.Add(new StudioOutput
        {
            Id = "output-virtual-camera",
            DisplayName = "Câmera virtual",
            TypeId = "output.virtual-camera",
            Destination = "Dispositivo de câmera virtual",
            Codec = "NV12",
            Bitrate = "60 fps",
            AssignedSceneId = "scene-interview",
            DefaultTransitionId = "transition-cut",
            TransitionDurationMs = 0,
            IsConfigured = false,
            State = StudioOutputState.Planned
        });
    }

    private static void SeedScenes(StudioDocument document)
    {
        var main = new StudioScene
        {
            Id = "scene-main",
            DisplayName = "Cena principal",
            Metadata = "1920×1080 • 60 fps",
            IsProgram = true
        };
        main.OutputIds.Add("output-preview");
        main.OutputIds.Add("output-recording");
        main.OutputIds.Add("output-rtmp-twitch");
        main.Effects.Add(CreateSceneEffect("scene-main-effect-color", "Correção de cor", "Ajuste global planejado para a cena principal.", false));
        main.Layers.Add(CreateLayer("layer-desktop", "Captura de tela", "source-desktop-1", "Captura de tela", "Source", 1, 54, 46, 1812, 860, 100));
        main.Layers.Add(CreateLayer("layer-webcam", "Webcam", "source-webcam", "Webcam", "Source", 2, 1378, 106, 410, 232, 100));
        main.Layers.Add(CreateLayer("layer-logo", "Logo.png", "source-logo", "Logo.png", "Image", 3, 1664, 926, 176, 104, 96));
        main.Layers.Add(CreateLayer("layer-lower-third", "Tarja inferior", "source-lower-third", "Tarja inferior", "Text", 4, 192, 820, 1280, 148, 92));
        document.Scenes.Add(main);

        var interview = new StudioScene
        {
            Id = "scene-interview",
            DisplayName = "Interview",
            Metadata = "1920×1080 • 60 fps"
        };
        interview.OutputIds.Add("output-virtual-camera");
        interview.Effects.Add(CreateSceneEffect("scene-interview-effect-soft", "Desfoque de fundo", "Planejado para cenas com câmera em destaque.", false));
        interview.Layers.Add(CreateLayer("layer-interview-desktop", "Captura de tela", "source-desktop-1", "Captura de tela", "Source", 1, 80, 82, 1120, 630, 100));
        interview.Layers.Add(CreateLayer("layer-interview-webcam", "Webcam", "source-webcam", "Webcam", "Source", 2, 1230, 102, 560, 315, 100));
        interview.Layers.Add(CreateLayer("layer-interview-logo", "Logo.png", "source-logo", "Logo.png", "Image", 3, 1540, 870, 220, 130, 92));
        document.Scenes.Add(interview);

        var brb = new StudioScene
        {
            Id = "scene-brb",
            DisplayName = "Break BRB",
            Metadata = "1920×1080 • 60 fps"
        };
        brb.Effects.Add(CreateSceneEffect("scene-brb-effect-lut", "LUT da cena", "Planejado para telas de espera.", false));
        brb.Layers.Add(CreateLayer("layer-brb-background", "Intro.mp4", "source-intro", "Intro.mp4", "Source", 1, 0, 0, 1920, 1080, 100));
        brb.Layers.Add(CreateLayer("layer-brb-logo", "Logo.png", "source-logo", "Logo.png", "Image", 2, 760, 280, 400, 240, 100));
        brb.Layers.Add(CreateLayer("layer-brb-text", "Tarja inferior", "source-lower-third", "Tarja inferior", "Text", 3, 520, 660, 880, 160, 95));
        document.Scenes.Add(brb);
    }

    private static StudioEffect CreateSceneEffect(string id, string name, string description, bool isEnabled)
    {
        return new StudioEffect
        {
            Id = id,
            Name = name,
            Description = description,
            IsEnabled = isEnabled
        };
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
            Description = "Remove fundo verde com suavidade e controle de spill.",
            IsEnabled = name == "Webcam",
            IsExpanded = name == "Webcam"
        });
        layer.Effects.Add(new StudioEffect
        {
            Id = $"{id}-effect-blur",
            Name = "Desfoque",
            Description = "Planejado para suavizar esta camada.",
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
            DisplayName = "Streaming 1080p",
            Metadata = "16:9 / 60 fps",
            TypeId = "preset.canvas-output"
        });
        document.Presets.Add(new StudioPreset
        {
            Id = "preset-youtube-1080p60",
            DisplayName = "YouTube 1080p60",
            Metadata = "H.264 perfil alto",
            TypeId = "preset.output"
        });
    }

    private static void SeedPackages(StudioDocument document)
    {
        document.Packages.Add(new StudioPackage
        {
            Id = "package-starter",
            DisplayName = "Pacote inicial",
            Metadata = "Cenas e modelos de fonte",
            TypeId = "package.scene"
        });
        document.Packages.Add(new StudioPackage
        {
            Id = "package-brand-kit",
            DisplayName = "Kit de marca",
            Metadata = "Tarjas e logotipos",
            TypeId = "package.brand"
        });
    }
}
