using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Audio;

public enum AudioSourceKind
{
    GeneratedTone = 0,
    Silence = 1,
    Test = 2,
    PhysicalCapture = 3,
    File = 4,
    Network = 5,
    RemoteScene = 6,
    VirtualDevice = 7
}

public enum AudioNodeKind
{
    Gain = 0,
    Mute = 1,
    Pan = 2,
    Polarity = 3,
    ChannelMapper = 4,
    Mixer = 5,
    PeakRmsMeter = 6,
    FixedDelay = 7
}

public enum AudioSinkKind
{
    ProgramMix = 0,
    PhysicalPlayback = 1,
    EncodedTrack = 2,
    VirtualDevice = 3
}

public sealed class AudioGraphDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public AudioFormat Format { get; set; } = AudioFormat.Stereo;
    public AudioQuantum Quantum { get; set; } = AudioQuantum.Default;
    public List<AudioSourceDefinition> Sources { get; set; } = [];
    public List<AudioNodeDefinition> Nodes { get; set; } = [];
    public List<AudioConnection> Connections { get; set; } = [];
    public List<AudioBusDefinition> Buses { get; set; } = [];
    public List<AudioOutputRoute> OutputRoutes { get; set; } = [];
    public List<AudioSinkDefinition> Sinks { get; set; } = [];
}

public sealed class AudioSourceDefinition
{
    public AudioSourceId Id { get; set; } = AudioSourceId.New();
    public string Name { get; set; } = string.Empty;
    public AudioSourceKind Kind { get; set; } = AudioSourceKind.Silence;
    public AudioFormat Format { get; set; } = AudioFormat.Stereo;
    public bool Enabled { get; set; } = true;
    public double ToneFrequencyHz { get; set; } = 440d;
}

public sealed class AudioNodeDefinition
{
    public AudioNodeId Id { get; set; } = AudioNodeId.New();
    public string Name { get; set; } = string.Empty;
    public AudioNodeKind Kind { get; set; } = AudioNodeKind.Gain;
    public AudioFormat Format { get; set; } = AudioFormat.Stereo;
    public bool Enabled { get; set; } = true;
    public float Value { get; set; } = 1f;
}

public sealed class AudioConnection
{
    public AudioSourceId? SourceId { get; set; }
    public AudioNodeId? FromNodeId { get; set; }
    public AudioNodeId ToNodeId { get; set; }
}

public sealed class AudioBusDefinition
{
    public AudioBusId Id { get; set; } = AudioBusId.New();
    public string Name { get; set; } = string.Empty;
    public AudioFormat Format { get; set; } = AudioFormat.Stereo;
    public List<AudioNodeId> InputNodeIds { get; set; } = [];
}

public sealed class AudioOutputRoute
{
    public AudioOutputRouteId Id { get; set; } = AudioOutputRouteId.New();
    public AudioBusId BusId { get; set; }
    public AudioSinkId SinkId { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class AudioSinkDefinition
{
    public AudioSinkId Id { get; set; } = AudioSinkId.New();
    public string Name { get; set; } = string.Empty;
    public AudioSinkKind Kind { get; set; } = AudioSinkKind.ProgramMix;
    public AudioFormat Format { get; set; } = AudioFormat.Stereo;
    public bool Enabled { get; set; } = true;
}
