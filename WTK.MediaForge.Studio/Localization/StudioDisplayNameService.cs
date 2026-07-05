using WTK.MediaForge.Studio.Models;

namespace WTK.MediaForge.Studio.Localization;

public interface IStudioDisplayNameService
{
    string GetSourceTypeName(string typeId);

    string GetOutputTypeName(string typeId);

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
            "source.desktop" => "Captura de tela",
            "source.window" => "Janela",
            "source.image" => "Imagem",
            "source.media" => "Arquivo de midia",
            "source.text" => "Texto",
            "source.solid" => "Cor solida",
            "source.ndi" => "NDI",
            "source.rtsp" => "RTSP / Camera IP",
            _ => HumanizeTypeId(typeId)
        };
    }

    public string GetOutputTypeName(string typeId)
    {
        return typeId switch
        {
            "output.preview" => "Preview",
            "output.file.mp4" => "Gravacao MP4",
            "output.rtmp" => "Transmissao RTMP",
            "output.srt" => "Transmissao SRT",
            "output.ndi" => "NDI",
            "output.virtual-camera" => "Camera virtual",
            _ => HumanizeTypeId(typeId)
        };
    }

    public string GetOutputStateName(StudioOutputUiState state)
    {
        return state switch
        {
            StudioOutputUiState.NotConfigured => "Nao configurada",
            StudioOutputUiState.Ready => "Pronta",
            StudioOutputUiState.Starting => "Iniciando",
            StudioOutputUiState.Running => "Ativa",
            StudioOutputUiState.Stopping => "Parando",
            StudioOutputUiState.Error => "Erro",
            StudioOutputUiState.Planned => "Planejada",
            _ => state.ToString()
        };
    }

    public string GetOutputMonitorStateName(StudioOutputState state)
    {
        return state switch
        {
            StudioOutputState.Planned => "Planejada",
            StudioOutputState.Running => "Ativa",
            StudioOutputState.Recording => "Gravando",
            StudioOutputState.Live => "Ao vivo",
            StudioOutputState.Warning => "Atencao",
            StudioOutputState.Offline => "Offline",
            _ => state.ToString()
        };
    }

    public string GetHealthName(StudioHealthState state)
    {
        return state switch
        {
            StudioHealthState.Healthy => "Saudavel",
            StudioHealthState.Warning => "Atencao",
            StudioHealthState.Error => "Erro",
            StudioHealthState.Planned => "Planejado",
            StudioHealthState.Disabled => "Desabilitado",
            _ => state.ToString()
        };
    }

    public string GetLayerTypeName(string layerType)
    {
        return layerType switch
        {
            "Text" => "Texto",
            "Image" => "Imagem",
            "Source" => "Fonte",
            "Solid" => "Cor solida",
            _ => layerType
        };
    }

    public string GetBlendModeName(StudioBlendMode mode)
    {
        return mode switch
        {
            StudioBlendMode.Alpha => "Normal",
            StudioBlendMode.Additive => "Adicionar",
            StudioBlendMode.Multiply => "Multiplicar",
            StudioBlendMode.Screen => "Tela",
            _ => mode.ToString()
        };
    }

    private static string HumanizeTypeId(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
        {
            return "Desconhecido";
        }

        var last = typeId.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? typeId;
        return string.Join(' ', last.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
