using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Localization;

public interface IStudioDisplayNameService
{
    string GetSourceTypeName(string typeId);

    string GetOutputTypeName(string typeId);

    string GetEngineStateName(StudioEngineUiState state);

    string GetOutputStateName(StudioOutputUiState state);

    string GetOutputMonitorStateName(StudioOutputState state);

    string GetHealthName(StudioHealthState state);

    string GetLayerTypeName(string layerType);

    string GetBlendModeName(StudioBlendMode mode);
}

public sealed class StudioDisplayNameService : IStudioDisplayNameService
{
    public string GetSourceTypeName(string typeId)
    {
        return typeId switch
        {
            "source.webcam" => "Webcam",
            "source.desktop" => "Desktop Capture",
            "source.window" => "Window Capture",
            "source.image" => "Image",
            "source.media" => "Media File",
            "source.text" => "Text",
            "source.solid" => "Solid Color",
            "source.ndi" => "NDI",
            "source.rtsp" => "RTSP / IP Camera",
            _ => HumanizeTypeId(typeId)
        };
    }

    public string GetOutputTypeName(string typeId)
    {
        return typeId switch
        {
            "output.preview" => "Preview",
            "output.file.mp4" => "Recording MP4",
            "output.rtmp" => "RTMP Streaming",
            "output.srt" => "SRT Streaming",
            "output.ndi" => "NDI",
            "output.virtual-camera" => "Virtual Camera",
            _ => HumanizeTypeId(typeId)
        };
    }

    public string GetEngineStateName(StudioEngineUiState state)
    {
        return state switch
        {
            StudioEngineUiState.Starting => "Starting",
            StudioEngineUiState.Running => "Running",
            StudioEngineUiState.Stopping => "Stopping",
            StudioEngineUiState.Failed => "Needs attention",
            _ => "Stopped"
        };
    }

    public string GetOutputStateName(StudioOutputUiState state)
    {
        return state switch
        {
            StudioOutputUiState.NotConfigured => "Not configured",
            StudioOutputUiState.Ready => "Ready",
            StudioOutputUiState.Starting => "Starting",
            StudioOutputUiState.Running => "Running",
            StudioOutputUiState.Stopping => "Stopping",
            StudioOutputUiState.Error => "Error",
            StudioOutputUiState.Planned => "Planned",
            _ => state.ToString()
        };
    }

    public string GetOutputMonitorStateName(StudioOutputState state)
    {
        return state switch
        {
            StudioOutputState.Planned => "Planned",
            StudioOutputState.Running => "Running",
            StudioOutputState.Recording => "Recording",
            StudioOutputState.Live => "Live",
            StudioOutputState.Warning => "Warning",
            StudioOutputState.Offline => "Offline",
            _ => state.ToString()
        };
    }

    public string GetHealthName(StudioHealthState state)
    {
        return state switch
        {
            StudioHealthState.Healthy => "Healthy",
            StudioHealthState.Warning => "Warning",
            StudioHealthState.Error => "Error",
            StudioHealthState.Planned => "Planned",
            StudioHealthState.Disabled => "Disabled",
            _ => state.ToString()
        };
    }

    public string GetLayerTypeName(string layerType)
    {
        return layerType switch
        {
            "Text" => "Text",
            "Image" => "Image",
            "Source" => "Source",
            "Solid" => "Solid Color",
            _ => layerType
        };
    }

    public string GetBlendModeName(StudioBlendMode mode)
    {
        return mode switch
        {
            StudioBlendMode.Alpha => "Normal",
            StudioBlendMode.Additive => "Add",
            StudioBlendMode.Multiply => "Multiply",
            StudioBlendMode.Screen => "Screen",
            _ => mode.ToString()
        };
    }

    private static string HumanizeTypeId(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            return "Unknown";
        }

        var last = typeId.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? typeId;
        return string.Join(' ', last.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
