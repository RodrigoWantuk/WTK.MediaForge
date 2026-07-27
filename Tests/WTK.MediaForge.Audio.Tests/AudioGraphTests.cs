using WTK.MediaForge.Audio;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Audio.Tests;

public sealed class AudioGraphTests
{
    [Fact]
    public void Valid_graph_compiles_deterministically_and_reports_source_fanout()
    {
        var graph = CreateValidGraph();
        var first = AudioGraphCompiler.Compile(graph);
        var second = AudioGraphCompiler.Compile(graph);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(2, first.SourceConsumerCounts[graph.Sources[0].Id]);
        Assert.Equal(graph.Nodes.Count, first.TopologicalNodeIds.Count);
    }

    [Fact]
    public void Cycle_is_rejected()
    {
        var graph = CreateValidGraph();
        graph.Connections.Add(new AudioConnection
        {
            FromNodeId = graph.Nodes[0].Id,
            ToNodeId = graph.Nodes[1].Id
        });
        graph.Connections.Add(new AudioConnection
        {
            FromNodeId = graph.Nodes[1].Id,
            ToNodeId = graph.Nodes[0].Id
        });

        Assert.Contains(AudioGraphValidator.Validate(graph).Issues, issue => issue.Code == "audio.graph.cycle");
    }

    [Fact]
    public void Unavailable_physical_sources_and_sinks_have_explicit_diagnostics()
    {
        var graph = CreateValidGraph();
        graph.Sources[0].Kind = AudioSourceKind.PhysicalCapture;
        graph.Sinks[0].Kind = AudioSinkKind.PhysicalPlayback;

        var issues = AudioGraphValidator.Validate(graph).Issues;
        Assert.Contains(issues, issue => issue.Code == "audio.source.unavailable");
        Assert.Contains(issues, issue => issue.Code == "audio.sink.unavailable");
    }

    [Theory]
    [InlineData(AudioQuantum.FiveMillisecondsFrames)]
    [InlineData(AudioQuantum.DefaultFrames)]
    [InlineData(AudioQuantum.TwentyMillisecondsFrames)]
    public void First_backend_quantums_are_supported(int frames) => new AudioQuantum(frames).Validate(allowModelOnlyQuantum: false);

    [Fact]
    public void Runtime_swaps_plan_between_blocks_and_returns_pool_to_baseline()
    {
        var runtime = new AudioRuntime(new AudioBufferPool(maximumRetainedBlocks: 2));
        runtime.Publish(AudioGraphCompiler.Compile(CreateValidGraph()));
        runtime.Start();
        using (var block = runtime.ProcessSilence(new AudioTimestamp(1), 1))
        {
            Assert.Equal(AudioBlockFlags.Silence, block.Block.Flags);
            Assert.Equal(1, runtime.GetHealth().RentedBlocks);
        }

        runtime.Publish(AudioGraphCompiler.Compile(CreateValidGraph()));
        var health = runtime.GetHealth();
        Assert.Equal(0, health.RentedBlocks);
        Assert.Equal(1, health.RetiredPlanCount);
        Assert.True(health.IsRunning);
    }

    [Fact]
    public void Dsp_and_meter_are_deterministic()
    {
        var pool = new AudioBufferPool();
        using var lease = pool.Rent(AudioFormat.Stereo, AudioQuantum.Default, new AudioTimestamp(0), 0);
        lease.Block.Channels[0][0] = 1f;
        lease.Block.Channels[1][0] = -1f;
        AudioDsp.Apply(new AudioNodeDefinition { Kind = AudioNodeKind.Gain, Value = .5f }, lease.Block);

        var meter = AudioDsp.Measure(lease.Block);
        Assert.Equal(.5f, meter.Peak);
        Assert.True(meter.Rms > 0f);
    }

    private static AudioGraphDefinition CreateValidGraph()
    {
        var source = new AudioSourceDefinition { Id = AudioSourceId.New(), Name = "Tone", Kind = AudioSourceKind.GeneratedTone };
        var gain = new AudioNodeDefinition { Id = AudioNodeId.New(), Name = "Gain", Kind = AudioNodeKind.Gain };
        var meter = new AudioNodeDefinition { Id = AudioNodeId.New(), Name = "Meter", Kind = AudioNodeKind.PeakRmsMeter };
        var bus = new AudioBusDefinition { Id = AudioBusId.New(), Name = "Program", InputNodeIds = [gain.Id, meter.Id] };
        var sink = new AudioSinkDefinition { Id = AudioSinkId.New(), Name = "Program mix", Kind = AudioSinkKind.ProgramMix };
        return new AudioGraphDefinition
        {
            Sources = [source],
            Nodes = [gain, meter],
            Connections =
            [
                new AudioConnection { SourceId = source.Id, ToNodeId = gain.Id },
                new AudioConnection { SourceId = source.Id, ToNodeId = meter.Id }
            ],
            Buses = [bus],
            Sinks = [sink],
            OutputRoutes = [new AudioOutputRoute { Id = AudioOutputRouteId.New(), BusId = bus.Id, SinkId = sink.Id }]
        };
    }
}
