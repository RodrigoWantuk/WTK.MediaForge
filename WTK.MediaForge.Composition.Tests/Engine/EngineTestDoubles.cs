using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Tests.Engine;

internal sealed class RecordingRenderBackendFactory : IRenderBackendFactory
{
    public NullRenderBackend? Backend { get; private set; }

    public int CreateAttempts { get; private set; }

    public bool ShouldFail { get; set; }

    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        CreateAttempts++;
        if (ShouldFail)
        {
            backend = null;
            return false;
        }

        Backend = new NullRenderBackend(threadGuard);
        backend = Backend;
        return true;
    }
}

internal sealed class FakeRenderOutputSink : IRenderOutputSink
{
    public RenderOutputTarget Target { get; }

    public FakeRenderOutputSink(RenderOutputTarget target) => Target = target;

    public RenderOutputBindingSnapshot CreateBinding(
        RenderOutputId outputId,
        FrameSize surfaceSize,
        long bindingVersion) =>
        new()
        {
            OutputId = outputId,
            TargetKind = Target.TypeId == RenderOutputTypes.Offscreen
                ? RenderTargetKind.Offscreen
                : RenderTargetKind.Win32Hwnd,
            NativeHandle = Target is WinFormsPreviewRenderOutputTarget preview
                ? preview.WindowHandle
                : 0,
            SurfaceSize = surfaceSize,
            BindingVersion = bindingVersion
        };

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeRenderOutputSinkFactory : IRenderOutputSinkFactory
{
    public bool CanCreate(RenderOutputTypeId typeId) =>
        typeId == RenderOutputTypes.PreviewWindow || typeId == RenderOutputTypes.Offscreen;

    public IRenderOutputSink CreateSink(RenderOutputTarget target) => new FakeRenderOutputSink(target);
}

internal sealed class FakeMediaSourceProviderFactory : IMediaSourceProviderFactory
{
    private readonly Dictionary<SourceId, FakeVideoFrameSource> _sources = new();
    private int _createCount;

    public int CreateCount => _createCount;

    public bool FailOnCreateAfter { get; set; }

    public int FailAfterCount { get; set; } = int.MaxValue;

    public IReadOnlyDictionary<SourceId, FakeVideoFrameSource> Sources => _sources;

    public bool CanCreate(MediaSourceTypeId typeId) => true;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        _createCount++;
        if (_createCount > FailAfterCount)
            throw new InvalidOperationException("Simulated provider factory failure.");

        var provider = new FakeVideoFrameSource(
            sourceDefinition.Id,
            sourceDefinition.Name,
            new FrameSize(640, 480));

        _sources[sourceDefinition.Id] = provider;
        return provider;
    }
}

internal sealed class SpyVideoFrameProvider : IVideoFrameProvider
{
    private readonly IVideoFrameProvider _inner;

    public SpyVideoFrameProvider(IVideoFrameProvider inner) => _inner = inner;

    public SourceId Id => _inner.Id;

    public string Name => _inner.Name;

    public MediaSourceState State => _inner.State;

    public Exception? LastError => _inner.LastError;

    public List<string> StopOrderLog { get; } = [];

    public Task StartAsync(CancellationToken cancellationToken) => _inner.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopOrderLog.Add(Name);
        return _inner.StopAsync(cancellationToken);
    }

    public bool TryAcquireLatestFrame(out GpuFrameLease lease) =>
        _inner.TryAcquireLatestFrame(out lease);
}
