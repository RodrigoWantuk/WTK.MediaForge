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
            if (bus.Id.IsEmpty)
                issues.Add(new("audio.bus.id.empty", "Audio bus id cannot be empty."));
            foreach (var nodeId in bus.InputNodeIds)
                if (nodeId.IsEmpty || !nodes.Contains(nodeId))
                    issues.Add(new("audio.bus.input.missing", "Audio bus references a missing input node."));
        }

        foreach (var sink in graph.Sinks)
        {
            ValidateFormat(sink.Format, "audio.sink.format", issues);
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

public sealed record AudioPhysicalGraphPlan(
    AudioGraphDefinition Graph,
    IReadOnlyList<AudioNodeId> TopologicalNodeIds,
    IReadOnlyDictionary<AudioSourceId, int> SourceConsumerCounts,
    TimeSpan Latency,
    string Fingerprint);

public static class AudioGraphCompiler
{
    public static AudioPhysicalGraphPlan Compile(AudioGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        AudioGraphValidator.Validate(graph).ThrowIfInvalid();
        var dependencies = graph.Nodes.ToDictionary(static node => node.Id, static _ => 0);
        var outgoing = graph.Nodes.ToDictionary(static node => node.Id, static _ => new List<AudioNodeId>());
        foreach (var connection in graph.Connections.Where(static connection => connection.FromNodeId is not null))
        {
            dependencies[connection.ToNodeId]++;
            outgoing[connection.FromNodeId!.Value].Add(connection.ToNodeId);
        }
        var ready = new SortedSet<AudioNodeId>(Comparer<AudioNodeId>.Create(static (left, right) => left.Value.CompareTo(right.Value)));
        foreach (var pair in dependencies.Where(static pair => pair.Value == 0))
            ready.Add(pair.Key);
        var ordered = new List<AudioNodeId>(graph.Nodes.Count);
        while (ready.Count > 0)
        {
            var node = ready.Min;
            ready.Remove(node);
            ordered.Add(node);
            foreach (var child in outgoing[node].OrderBy(static value => value.Value))
                if (--dependencies[child] == 0)
                    ready.Add(child);
        }
        var fanOut = graph.Connections.Where(static connection => connection.SourceId is not null)
            .GroupBy(static connection => connection.SourceId!.Value)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var canonical = string.Join('|', ordered.Select(static id => id.Value.ToString("N"))) + ";" +
            string.Join('|', fanOut.OrderBy(static pair => pair.Key.Value).Select(static pair => $"{pair.Key.Value:N}:{pair.Value}")) + ";" +
            graph.Format + ";" + graph.Quantum.Frames;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var latency = TimeSpan.FromTicks(graph.Nodes.Count(static node => node.Kind == AudioNodeKind.FixedDelay) * graph.Quantum.Duration.Ticks);
        return new AudioPhysicalGraphPlan(graph, ordered, fanOut, latency, fingerprint);
    }
}
