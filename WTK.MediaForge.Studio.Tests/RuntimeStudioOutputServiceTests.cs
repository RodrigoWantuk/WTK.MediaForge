using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Studio.Models;
using WTK.MediaForge.Studio.Services;
using Xunit;

namespace WTK.MediaForge.Studio.Tests;

public sealed class RuntimeStudioOutputServiceTests
{
    [Fact]
    public async Task Capability_block_does_not_become_operational_error()
    {
        var engine = new RecordingOutputEngine(CreateProject());
        var service = new RuntimeStudioOutputService(engine, new OutputCapabilities(supported: false));

        await service.ToggleStreamingAsync(CancellationToken.None);

        Assert.Equal(StudioOutputUiState.NotConfigured, service.StreamingState);
        Assert.Equal(0, engine.StartCount);
    }

    [Fact]
    public async Task Recording_elapsed_uses_real_wall_clock_and_restart_rolls_segment()
    {
        var engine = new RecordingOutputEngine(CreateProject());
        var service = new RuntimeStudioOutputService(engine, new OutputCapabilities(supported: true));

        await service.ToggleRecordingAsync(CancellationToken.None);
        await Task.Delay(25);
        var firstElapsed = service.RecordingElapsed;
        await service.ToggleRecordingAsync(CancellationToken.None);
        await service.ToggleRecordingAsync(CancellationToken.None);

        Assert.True(firstElapsed > TimeSpan.Zero);
        Assert.Equal(StudioOutputUiState.Running, service.RecordingState);
        Assert.EndsWith("capture.segment-0002.mp4", Assert.Single(engine.RecordingPaths), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rtmp_start_failure_does_not_stop_running_mp4()
    {
        var project = CreateProject();
        var engine = new RecordingOutputEngine(project) { FailStreamingStart = true };
        engine.AddRunning(project.Outputs.Single(output => output.TypeId == RenderOutputTypes.RecordingMp4).Id);
        var service = new RuntimeStudioOutputService(engine, new OutputCapabilities(supported: true));
        service.RefreshStatus();

        await service.ToggleStreamingAsync(CancellationToken.None);

        Assert.Equal(StudioOutputUiState.Error, service.StreamingState);
        Assert.Equal(StudioOutputUiState.Running, service.RecordingState);
        Assert.Single(engine.GetSnapshots());
    }

    private static MediaForgeProject CreateProject()
    {
        var canvasId = CanvasId.New();
        return new MediaForgeProject
        {
            Canvases =
            {
                new MediaForgeCanvas { Id = canvasId, Name = "Program", Size = new FrameSize(1920, 1080) }
            },
            Outputs =
            {
                new MediaForgeRenderOutput
                {
                    Id = RenderOutputId.New(), Name = "Recording", CanvasId = canvasId,
                    TypeId = RenderOutputTypes.RecordingMp4, OutputSize = new FrameSize(1920, 1080),
                    Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4("capture.mp4"))
                },
                new MediaForgeRenderOutput
                {
                    Id = RenderOutputId.New(), Name = "Streaming", CanvasId = canvasId,
                    TypeId = RenderOutputTypes.StreamingRtmp, OutputSize = new FrameSize(1920, 1080),
                    Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.Rtmp("rtmp://localhost/live", "key"))
                }
            }
        };
    }

    private sealed class RecordingOutputEngine(MediaForgeProject project) : IStudioEncodedOutputEngine
    {
        private readonly List<EncodedOutputRuntimeSnapshot> _snapshots = [];
        public MediaForgeEngineState State => MediaForgeEngineState.Running;
        public MediaForgeProject? CurrentProject { get; } = project;
        public int StartCount { get; private set; }
        public bool FailStreamingStart { get; init; }
        public List<string> RecordingPaths { get; } = [];
        public IReadOnlyList<EncodedOutputRuntimeSnapshot> GetSnapshots() => _snapshots.ToArray();
        public void AddRunning(RenderOutputId outputId) => _snapshots.Add(CreateRunning(outputId));
        public Task StartAsync(RenderOutputId outputId, CancellationToken cancellationToken)
        {
            StartCount++;
            var output = CurrentProject!.Outputs.Single(candidate => candidate.Id == outputId);
            if (FailStreamingStart && output.TypeId == RenderOutputTypes.StreamingRtmp)
                throw new InvalidOperationException("RTMP connection failed");
            _snapshots.Add(CreateRunning(outputId));
            return Task.CompletedTask;
        }
        public Task StopAsync(RenderOutputId outputId, CancellationToken cancellationToken)
        {
            _snapshots.RemoveAll(snapshot => snapshot.OutputId == outputId);
            return Task.CompletedTask;
        }
        public Task SetRecordingPathAsync(RenderOutputId outputId, RecordingMp4OutputSettings settings, string path, CancellationToken cancellationToken)
        {
            RecordingPaths.Add(path);
            CurrentProject!.Outputs.Single(candidate => candidate.Id == outputId).Settings =
                RenderOutputSettingsSerializer.ToJson(new RecordingMp4OutputSettings { Path = path, Video = settings.Video });
            return Task.CompletedTask;
        }
        private static EncodedOutputRuntimeSnapshot CreateRunning(RenderOutputId outputId) =>
            new(outputId, EncodedOutputRuntimeStatus.Running, null, 1, 1, 1, 0, TimeSpan.FromMilliseconds(2));
    }

    private sealed class OutputCapabilities(bool supported) : IStudioCapabilityService
    {
        private readonly StudioCapabilityStatus _status = supported ? StudioCapabilityStatus.Supported : StudioCapabilityStatus.Blocked;
        public IReadOnlyList<StudioCapabilityDescriptor> GetSourceCapabilities() => [];
        public IReadOnlyList<StudioCapabilityDescriptor> GetOutputCapabilities() =>
        [
            new("output.file.mp4", "MP4", "Recording", StudioIconKind.Record, _status, "proof unavailable"),
            new("output.rtmp", "RTMP", "Streaming", StudioIconKind.Stream, _status, "proof unavailable")
        ];
    }
}
