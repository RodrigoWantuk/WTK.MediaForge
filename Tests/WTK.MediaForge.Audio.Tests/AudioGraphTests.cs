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

    [Fact]
    public void Incompatible_formats_are_rejected_until_a_converter_is_planned()
    {
        var graph = CreateValidGraph();
        graph.Sources[0].Format = AudioFormat.Mono;

        Assert.Contains(AudioGraphValidator.Validate(graph).Issues,
            issue => issue.Code == "audio.source.format.incompatible");
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
    public void Published_plan_is_immutable_and_fingerprint_includes_graph_parameters()
    {
        var graph = CreateValidGraph();
        var plan = AudioGraphCompiler.Compile(graph);
        var fingerprint = plan.Fingerprint;

        graph.Sources[0].ToneFrequencyHz = 880d;
        graph.Nodes[0].Value = .25f;

        Assert.Equal(440d, plan.Graph.Sources[0].ToneFrequencyHz);
        Assert.Equal(1f, plan.Graph.Nodes[0].Value);
        Assert.NotEqual(fingerprint, AudioGraphCompiler.Compile(graph).Fingerprint);
    }

    [Fact]
    public void Runtime_renders_deterministic_generated_tone_from_prepared_pool()
    {
        var graph = CreateValidGraph();
        graph.Sources[0].ToneFrequencyHz = 1_000d;
        var pool = new AudioBufferPool(maximumRetainedBlocks: 2);
        var runtime = new AudioRuntime(pool);
        runtime.Publish(AudioGraphCompiler.Compile(graph));
        runtime.Start();

        using var first = runtime.ProcessSource(graph.Sources[0].Id, new AudioTimestamp(0), 0);
        Assert.Equal(2, pool.PreparedBlockCount);
        Assert.Equal(0f, first.Block.Channels[0][0]);
        Assert.NotEqual(0f, first.Block.Channels[0][1]);
        Assert.Equal(first.Block.Channels[0][1], first.Block.Channels[1][1]);
    }

    [Fact]
    public void Clock_synchronization_and_drift_adjustment_are_bounded()
    {
        var clock = new FixedClock(new AudioTimestamp(100));
        var synchronizer = new AudioClockSynchronizer(clock);
        Assert.Equal(0, synchronizer.GetSynchronizedTimestamp().MonotonicTicks);
        clock.Timestamp = new AudioTimestamp(200);
        Assert.Equal(100, synchronizer.GetSynchronizedTimestamp().MonotonicTicks);

        var coordinator = new AudioVideoSyncCoordinator();
        coordinator.Observe(new AudioTimestamp(TimeSpan.TicksPerSecond), new AudioTimestamp(0));
        Assert.InRange(coordinator.GetSuggestedResampleFrameAdjustment(AudioQuantum.Default), -4, 4);
    }

    [Fact]
    public void Runtime_processes_source_node_dag_into_a_program_bus()
    {
        var source = new AudioSourceDefinition { Id = AudioSourceId.New(), Kind = AudioSourceKind.GeneratedTone, ToneFrequencyHz = 1_000d };
        var gain = new AudioNodeDefinition { Id = AudioNodeId.New(), Kind = AudioNodeKind.Gain, Value = .5f };
        var bus = new AudioBusDefinition { Id = AudioBusId.New(), InputNodeIds = [gain.Id] };
        var sink = new AudioSinkDefinition { Id = AudioSinkId.New(), Kind = AudioSinkKind.ProgramMix };
        var graph = new AudioGraphDefinition
        {
            Sources = [source],
            Nodes = [gain],
            Connections = [new AudioConnection { SourceId = source.Id, ToNodeId = gain.Id }],
            Buses = [bus],
            Sinks = [sink],
            OutputRoutes = [new AudioOutputRoute { Id = AudioOutputRouteId.New(), BusId = bus.Id, SinkId = sink.Id }]
        };
        var runtime = new AudioRuntime(new AudioBufferPool(maximumRetainedBlocks: 4));
        runtime.Publish(AudioGraphCompiler.Compile(graph));
        runtime.Start();

        using (var mix = runtime.ProcessBus(bus.Id, new AudioTimestamp(0), 0))
        {
            Assert.Equal(0f, mix.Block.Channels[0][0]);
            Assert.InRange(mix.Block.Channels[0][1], .064f, .066f);
            Assert.Equal(1, runtime.GetHealth().RentedBlocks);
        }

        Assert.Equal(0, runtime.GetHealth().RentedBlocks);
    }

    [Fact]
    public void Fixed_delay_returns_the_previous_quantum_without_retaining_leases()
    {
        var source = new AudioSourceDefinition { Id = AudioSourceId.New(), Kind = AudioSourceKind.GeneratedTone, ToneFrequencyHz = 1_000d };
        var delay = new AudioNodeDefinition { Id = AudioNodeId.New(), Kind = AudioNodeKind.FixedDelay };
        var bus = new AudioBusDefinition { Id = AudioBusId.New(), InputNodeIds = [delay.Id] };
        var sink = new AudioSinkDefinition { Id = AudioSinkId.New(), Kind = AudioSinkKind.ProgramMix };
        var graph = new AudioGraphDefinition
        {
            Sources = [source], Nodes = [delay],
            Connections = [new AudioConnection { SourceId = source.Id, ToNodeId = delay.Id }],
            Buses = [bus], Sinks = [sink],
            OutputRoutes = [new AudioOutputRoute { Id = AudioOutputRouteId.New(), BusId = bus.Id, SinkId = sink.Id }]
        };
        var runtime = new AudioRuntime(new AudioBufferPool(maximumRetainedBlocks: 4));
        runtime.Publish(AudioGraphCompiler.Compile(graph));
        runtime.Start();

        using var first = runtime.ProcessBus(bus.Id, new AudioTimestamp(0), 0);
        using var second = runtime.ProcessBus(bus.Id, new AudioTimestamp(10), 1);

        Assert.Equal(0f, first.Block.Channels[0][1]);
        Assert.InRange(second.Block.Channels[0][1], .129f, .131f);
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

    private sealed class FixedClock(AudioTimestamp timestamp) : IAudioClock
    {
        public AudioTimestamp Timestamp { get; set; } = timestamp;
        public AudioTimestamp GetTimestamp() => Timestamp;
    }
}
