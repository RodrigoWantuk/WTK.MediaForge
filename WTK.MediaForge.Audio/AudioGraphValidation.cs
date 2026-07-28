using System.Security.Cryptography;
using System.Text;
using WTK.MediaForge.Core.Identifiers;

namespace WTK.MediaForge.Audio;

public sealed record AudioGraphValidationIssue(string Code, string Message);

public sealed class AudioGraphValidationResult(IReadOnlyList<AudioGraphValidationIssue> issues)
{
    public IReadOnlyList<AudioGraphValidationIssue> Issues { get; } = issues;
    public bool IsValid => Issues.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new InvalidOperationException(string.Join(Environment.NewLine, Issues.Select(static issue => issue.Message)));
    }
}

public static class AudioGraphValidator
{
    public static AudioGraphValidationResult Validate(AudioGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var issues = new List<AudioGraphValidationIssue>();
        if (graph.SchemaVersion <= 0)
            issues.Add(new("audio.schema.invalid", "Audio graph schema version must be positive."));

        ValidateFormat(graph.Format, "audio.graph.format", issues);
        try { graph.Quantum.Validate(); }
        catch (ArgumentOutOfRangeException exception) { issues.Add(new("audio.quantum.invalid", exception.Message)); }

        ValidateUnique(graph.Sources.Select(static source => source.Id.Value), "audio.source", issues);
        ValidateUnique(graph.Nodes.Select(static node => node.Id.Value), "audio.node", issues);
        ValidateUnique(graph.Buses.Select(static bus => bus.Id.Value), "audio.bus", issues);
        ValidateUnique(graph.Sinks.Select(static sink => sink.Id.Value), "audio.sink", issues);
        ValidateUnique(graph.OutputRoutes.Select(static route => route.Id.Value), "audio.route", issues);

        var sources = graph.Sources.Select(static source => source.Id).ToHashSet();
        var nodes = graph.Nodes.Select(static node => node.Id).ToHashSet();
        var buses = graph.Buses.Select(static bus => bus.Id).ToHashSet();
        var sinks = graph.Sinks.Select(static sink => sink.Id).ToHashSet();

        foreach (var source in graph.Sources)
        {
            ValidateFormat(source.Format, "audio.source.format", issues);
            ValidateCompatibleFormat(graph.Format, source.Format, "audio.source.format.incompatible", "Audio source format must match the current graph format.", issues);
            if (source.Id.IsEmpty)
                issues.Add(new("audio.source.id.empty", "Audio source id cannot be empty."));
            if (source.Kind is AudioSourceKind.PhysicalCapture or AudioSourceKind.File or AudioSourceKind.Network or AudioSourceKind.RemoteScene or AudioSourceKind.VirtualDevice)
                issues.Add(new("audio.source.unavailable", $"Audio source '{source.Name}' requires a platform adapter and is unavailable."));
            if (source.Kind == AudioSourceKind.GeneratedTone && (!double.IsFinite(source.ToneFrequencyHz) || source.ToneFrequencyHz <= 0d))
                issues.Add(new("audio.source.tone.invalid", "Generated tone frequency must be finite and positive."));
        }

        foreach (var node in graph.Nodes)
        {
            ValidateFormat(node.Format, "audio.node.format", issues);
            ValidateCompatibleFormat(graph.Format, node.Format, "audio.node.format.incompatible", "Audio node format must match the current graph format until a converter is explicitly planned.", issues);
            if (node.Id.IsEmpty)
                issues.Add(new("audio.node.id.empty", "Audio node id cannot be empty."));
            if (!float.IsFinite(node.Value))
                issues.Add(new("audio.node.value.invalid", $"Audio node '{node.Name}' has a non-finite value."));
        }

        foreach (var connection in graph.Connections)
        {
            if (connection.ToNodeId.IsEmpty || !nodes.Contains(connection.ToNodeId))
                issues.Add(new("audio.connection.target.missing", "Audio connection has a missing target node."));
            var sourceSpecified = connection.SourceId is { } sourceId && !sourceId.IsEmpty;
            var nodeSpecified = connection.FromNodeId is { } fromNodeId && !fromNodeId.IsEmpty;
            if (sourceSpecified == nodeSpecified)
                issues.Add(new("audio.connection.origin.invalid", "Audio connection must reference exactly one source or node origin."));
            if (sourceSpecified && !sources.Contains(connection.SourceId!.Value))
                issues.Add(new("audio.connection.source.missing", "Audio connection references a missing source."));
            if (nodeSpecified && !nodes.Contains(connection.FromNodeId!.Value))
                issues.Add(new("audio.connection.node.missing", "Audio connection references a missing source node."));
        }

        foreach (var bus in graph.Buses)
        {
            ValidateFormat(bus.Format, "audio.bus.format", issues);
            ValidateCompatibleFormat(graph.Format, bus.Format, "audio.bus.format.incompatible", "Audio bus format must match the current graph format until a converter is explicitly planned.", issues);
            if (bus.Id.IsEmpty)
                issues.Add(new("audio.bus.id.empty", "Audio bus id cannot be empty."));
            foreach (var nodeId in bus.InputNodeIds)
                if (nodeId.IsEmpty || !nodes.Contains(nodeId))
                    issues.Add(new("audio.bus.input.missing", "Audio bus references a missing input node."));
        }

        foreach (var sink in graph.Sinks)
        {
            ValidateFormat(sink.Format, "audio.sink.format", issues);
            ValidateCompatibleFormat(graph.Format, sink.Format, "audio.sink.format.incompatible", "Audio sink format must match the current graph format until a converter is explicitly planned.", issues);
            if (sink.Id.IsEmpty)
                issues.Add(new("audio.sink.id.empty", "Audio sink id cannot be empty."));
            if (sink.Kind is not AudioSinkKind.ProgramMix)
                issues.Add(new("audio.sink.unavailable", $"Audio sink '{sink.Name}' requires a platform or encode backend and is unavailable."));
        }

        foreach (var route in graph.OutputRoutes)
        {
            if (route.Id.IsEmpty)
                issues.Add(new("audio.route.id.empty", "Audio output route id cannot be empty."));
            if (route.BusId.IsEmpty || !buses.Contains(route.BusId))
                issues.Add(new("audio.route.bus.missing", "Audio output route references a missing bus."));
            if (route.SinkId.IsEmpty || !sinks.Contains(route.SinkId))
                issues.Add(new("audio.route.sink.missing", "Audio output route references a missing sink."));
        }

        ValidateAcyclic(graph, issues);
        return new AudioGraphValidationResult(issues);
    }

    private static void ValidateAcyclic(AudioGraphDefinition graph, List<AudioGraphValidationIssue> issues)
    {
        var edges = graph.Connections
            .Where(static connection => connection.FromNodeId is not null && !connection.FromNodeId.Value.IsEmpty && !connection.ToNodeId.IsEmpty)
            .GroupBy(static connection => connection.FromNodeId!.Value)
            .ToDictionary(static group => group.Key, static group => group.Select(static connection => connection.ToNodeId).ToArray());
        var visiting = new HashSet<AudioNodeId>();
        var visited = new HashSet<AudioNodeId>();
        bool Visit(AudioNodeId node)
        {
            if (!visiting.Add(node))
                return true;
            if (edges.TryGetValue(node, out var destinations))
                foreach (var destination in destinations)
                    if (!visited.Contains(destination) && Visit(destination))
                        return true;
            visiting.Remove(node);
            visited.Add(node);
            return false;
        }

        if (graph.Nodes.Any(node => !visited.Contains(node.Id) && Visit(node.Id)))
            issues.Add(new("audio.graph.cycle", "Audio graph must be acyclic."));
    }

    private static void ValidateFormat(AudioFormat format, string code, List<AudioGraphValidationIssue> issues)
    {
        try { format.Validate(); }
        catch (ArgumentOutOfRangeException exception) { issues.Add(new(code, exception.Message)); }
    }

    private static void ValidateCompatibleFormat(
        AudioFormat graphFormat,
        AudioFormat candidate,
        string code,
        string message,
        List<AudioGraphValidationIssue> issues)
    {
        if (candidate != graphFormat)
            issues.Add(new(code, message));
    }

    private static void ValidateUnique(IEnumerable<Guid> ids, string prefix, List<AudioGraphValidationIssue> issues)
    {
        var seen = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (id == Guid.Empty)
                issues.Add(new($"{prefix}.id.empty", $"{prefix} id cannot be empty."));
            else if (!seen.Add(id))
                issues.Add(new($"{prefix}.id.duplicate", $"{prefix} id is duplicated."));
        }
    }
}

public sealed class AudioPhysicalGraphPlan
{
    private readonly AudioGraphDefinition _graph;
    private readonly IReadOnlyDictionary<AudioSourceId, AudioSourceDefinition> _sources;

    internal AudioPhysicalGraphPlan(
        AudioGraphDefinition graph,
        IReadOnlyList<AudioNodeId> topologicalNodeIds,
        IReadOnlyDictionary<AudioSourceId, int> sourceConsumerCounts,
        TimeSpan latency,
        string fingerprint)
    {
        _graph = graph;
        _sources = graph.Sources.ToDictionary(static source => source.Id);
        TopologicalNodeIds = topologicalNodeIds;
        SourceConsumerCounts = sourceConsumerCounts;
        Latency = latency;
        Fingerprint = fingerprint;
    }

    // A published plan is immutable from the runtime's perspective. Callers only
    // receive a copy, so editing the project model cannot alter an active graph.
    public AudioGraphDefinition Graph => AudioGraphCloner.Clone(_graph);
    public IReadOnlyList<AudioNodeId> TopologicalNodeIds { get; }
    public IReadOnlyDictionary<AudioSourceId, int> SourceConsumerCounts { get; }
    public TimeSpan Latency { get; }
    public string Fingerprint { get; }

    internal AudioGraphDefinition ExecutionGraph => _graph;

    internal bool TryGetSource(AudioSourceId sourceId, out AudioSourceDefinition source) =>
        _sources.TryGetValue(sourceId, out source!);
}

public static class AudioGraphCompiler
{
    public static AudioPhysicalGraphPlan Compile(AudioGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var snapshot = AudioGraphCloner.Clone(graph);
        AudioGraphValidator.Validate(snapshot).ThrowIfInvalid();
        var dependencies = snapshot.Nodes.ToDictionary(static node => node.Id, static _ => 0);
        var outgoing = snapshot.Nodes.ToDictionary(static node => node.Id, static _ => new List<AudioNodeId>());
        foreach (var connection in snapshot.Connections.Where(static connection => connection.FromNodeId is not null))
        {
            dependencies[connection.ToNodeId]++;
            outgoing[connection.FromNodeId!.Value].Add(connection.ToNodeId);
        }
        var ready = new SortedSet<AudioNodeId>(Comparer<AudioNodeId>.Create(static (left, right) => left.Value.CompareTo(right.Value)));
        foreach (var pair in dependencies.Where(static pair => pair.Value == 0))
            ready.Add(pair.Key);
        var ordered = new List<AudioNodeId>(snapshot.Nodes.Count);
        while (ready.Count > 0)
        {
            var node = ready.Min;
            ready.Remove(node);
            ordered.Add(node);
            foreach (var child in outgoing[node].OrderBy(static value => value.Value))
                if (--dependencies[child] == 0)
                    ready.Add(child);
        }
        var fanOut = snapshot.Connections.Where(static connection => connection.SourceId is not null)
            .GroupBy(static connection => connection.SourceId!.Value)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var canonical = BuildCanonicalSnapshot(snapshot, ordered, fanOut);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var latency = TimeSpan.FromTicks(snapshot.Nodes.Count(static node => node.Kind == AudioNodeKind.FixedDelay) * snapshot.Quantum.Duration.Ticks);
        return new AudioPhysicalGraphPlan(snapshot, ordered, fanOut, latency, fingerprint);
    }

    private static string BuildCanonicalSnapshot(
        AudioGraphDefinition graph,
        IReadOnlyList<AudioNodeId> ordered,
        IReadOnlyDictionary<AudioSourceId, int> fanOut) =>
        string.Join('|', ordered.Select(static id => id.Value.ToString("N"))) + ";" +
        string.Join('|', fanOut.OrderBy(static pair => pair.Key.Value).Select(static pair => $"{pair.Key.Value:N}:{pair.Value}")) + ";" +
        string.Join('|', graph.Sources.OrderBy(static source => source.Id.Value).Select(static source => FormattableString.Invariant($"S:{source.Id.Value:N}:{source.Kind}:{source.Format}:{source.Enabled}:{source.ToneFrequencyHz:R}"))) + ";" +
        string.Join('|', graph.Nodes.OrderBy(static node => node.Id.Value).Select(static node => FormattableString.Invariant($"N:{node.Id.Value:N}:{node.Kind}:{node.Format}:{node.Enabled}:{node.Value:R}"))) + ";" +
        string.Join('|', graph.Connections.OrderBy(static connection => connection.ToNodeId.Value).ThenBy(static connection => connection.SourceId?.Value).ThenBy(static connection => connection.FromNodeId?.Value).Select(static connection => $"C:{connection.SourceId?.Value:N}:{connection.FromNodeId?.Value:N}:{connection.ToNodeId.Value:N}")) + ";" +
        string.Join('|', graph.Buses.OrderBy(static bus => bus.Id.Value).Select(static bus => $"B:{bus.Id.Value:N}:{bus.Format}:{string.Join(',', bus.InputNodeIds.OrderBy(static id => id.Value).Select(static id => id.Value.ToString("N")))}")) + ";" +
        string.Join('|', graph.OutputRoutes.OrderBy(static route => route.Id.Value).Select(static route => $"R:{route.Id.Value:N}:{route.BusId.Value:N}:{route.SinkId.Value:N}:{route.Enabled}")) + ";" +
        string.Join('|', graph.Sinks.OrderBy(static sink => sink.Id.Value).Select(static sink => $"K:{sink.Id.Value:N}:{sink.Kind}:{sink.Format}:{sink.Enabled}")) + ";" +
        graph.SchemaVersion + ";" + graph.Format + ";" + graph.Quantum.Frames;
}

internal static class AudioGraphCloner
{
    public static AudioGraphDefinition Clone(AudioGraphDefinition graph) => new()
    {
        SchemaVersion = graph.SchemaVersion,
        Format = graph.Format,
        Quantum = graph.Quantum,
        Sources = graph.Sources.Select(static source => new AudioSourceDefinition
        {
            Id = source.Id, Name = source.Name, Kind = source.Kind, Format = source.Format,
            Enabled = source.Enabled, ToneFrequencyHz = source.ToneFrequencyHz
        }).ToList(),
        Nodes = graph.Nodes.Select(static node => new AudioNodeDefinition
        {
            Id = node.Id, Name = node.Name, Kind = node.Kind, Format = node.Format,
            Enabled = node.Enabled, Value = node.Value
        }).ToList(),
        Connections = graph.Connections.Select(static connection => new AudioConnection
        {
            SourceId = connection.SourceId, FromNodeId = connection.FromNodeId, ToNodeId = connection.ToNodeId
        }).ToList(),
        Buses = graph.Buses.Select(static bus => new AudioBusDefinition
        {
            Id = bus.Id, Name = bus.Name, Format = bus.Format, InputNodeIds = bus.InputNodeIds.ToList()
        }).ToList(),
        OutputRoutes = graph.OutputRoutes.Select(static route => new AudioOutputRoute
        {
            Id = route.Id, BusId = route.BusId, SinkId = route.SinkId, Enabled = route.Enabled
        }).ToList(),
        Sinks = graph.Sinks.Select(static sink => new AudioSinkDefinition
        {
            Id = sink.Id, Name = sink.Name, Kind = sink.Kind, Format = sink.Format, Enabled = sink.Enabled
        }).ToList()
    };
}
