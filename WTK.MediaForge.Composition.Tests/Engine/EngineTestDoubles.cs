using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
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

internal sealed class ManualRecordingRenderBackendFactory : IRenderBackendFactory
{
    public ManualNullRenderBackend? Backend { get; private set; }

    public int CreateAttempts { get; private set; }

    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        CreateAttempts++;
        Backend = new ManualNullRenderBackend(threadGuard);
        backend = Backend;
        return true;
    }
}

internal sealed class ThrowingDisposeRenderBackendFactory : IRenderBackendFactory
{
    public ThrowingDisposeRenderBackend? Backend { get; private set; }

    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        Backend = new ThrowingDisposeRenderBackend(threadGuard);
        backend = Backend;
        return true;
    }
}

internal sealed class UnbindTrackingRenderBackendFactory : IRenderBackendFactory
{
    public UnbindTrackingRenderBackend? Backend { get; private set; }

    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        Backend = new UnbindTrackingRenderBackend(threadGuard);
        backend = Backend;
        return true;
    }
}

internal sealed class UnbindTrackingRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;
    private readonly ManualResetEventSlim _unbindReceived = new(false);

    public UnbindTrackingRenderBackend(RenderThreadGuard threadGuard) =>
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

    public int UnbindCount => Volatile.Read(ref _unbindCount);

    public bool Disposed { get; private set; }

    private int _unbindCount;

    public void BindOutput(RenderOutputBindingSnapshot binding)
    {
        _threadGuard.AssertOnRenderThread();
    }

    public void UnbindOutput(RenderOutputId outputId)
    {
        _threadGuard.AssertOnRenderThread();
        Interlocked.Increment(ref _unbindCount);
        _unbindReceived.Set();
    }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize)
    {
        _threadGuard.AssertOnRenderThread();
    }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        _threadGuard.AssertOnRenderThread();
        return new ImmediateRenderFrameSubmission(snapshot);
    }

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public bool WaitForUnbind(TimeSpan timeout) => _unbindReceived.Wait(timeout);

    public void Dispose()
    {
        Disposed = true;
        _unbindReceived.Dispose();
    }
}

internal sealed class ThrowingDisposeRenderBackend : IRenderBackend
{
    private readonly NullRenderBackend _inner;

    public ThrowingDisposeRenderBackend(RenderThreadGuard threadGuard) =>
        _inner = new NullRenderBackend(threadGuard);

    public bool DisposeAttempted { get; private set; }

    public void BindOutput(RenderOutputBindingSnapshot binding) => _inner.BindOutput(binding);

    public void UnbindOutput(RenderOutputId outputId) => _inner.UnbindOutput(outputId);

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) =>
        _inner.ResizeOutput(outputId, surfaceSize);

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot) => _inner.Submit(snapshot);

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _inner.WaitIdleAsync(timeout, cancellationToken);

    public void Dispose()
    {
        DisposeAttempted = true;
        throw new InvalidOperationException("Simulated render backend dispose failure.");
    }
}

internal sealed class GpuFrameSlotRingSourceProviderFactory : IMediaSourceProviderFactory
{
    private readonly Dictionary<SourceId, FakeGpuFrameSlotRingVideoFrameSource> _sources = new();

    public IReadOnlyDictionary<SourceId, FakeGpuFrameSlotRingVideoFrameSource> Sources => _sources;

    public bool CanCreate(MediaSourceTypeId typeId) => true;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        var source = new FakeGpuFrameSlotRingVideoFrameSource(
            sourceDefinition.Id,
            sourceDefinition.Name,
            new FrameSize(640, 480));

        _sources[sourceDefinition.Id] = source;
        return new AutoCaptureGpuFrameProvider(source);
    }

    private sealed class AutoCaptureGpuFrameProvider(FakeGpuFrameSlotRingVideoFrameSource source) : IVideoFrameProvider
    {
        public SourceId Id => source.Id;

        public string Name => source.Name;

        public MediaSourceState State => source.State;

        public Exception? LastError => source.LastError;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await source.StartAsync(cancellationToken).ConfigureAwait(false);
            source.TryCaptureFrame(frameNumber: 1, MediaTime.Zero);
        }

        public Task StopAsync(CancellationToken cancellationToken) =>
            source.StopAsync(cancellationToken);

        public bool TryAcquireLatestFrame(out GpuFrameLease lease) =>
            source.TryAcquireLatestFrame(out lease);
    }
}

internal sealed class ThrowingStopMediaSourceProviderFactory : IMediaSourceProviderFactory
{
    private readonly FakeMediaSourceProviderFactory _inner = new();

    public FakeMediaSourceProviderFactory Inner => _inner;

    public bool CanCreate(MediaSourceTypeId typeId) => _inner.CanCreate(typeId);

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        var provider = _inner.CreateProvider(sourceDefinition);
        return new ThrowingStopProvider(provider);
    }

    private sealed class ThrowingStopProvider(IVideoFrameProvider inner) : IVideoFrameProvider
    {
        public SourceId Id => inner.Id;

        public string Name => inner.Name;

        public MediaSourceState State => inner.State;

        public Exception? LastError => inner.LastError;

        public Task StartAsync(CancellationToken cancellationToken) =>
            inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated provider stop failure.");

        public bool TryAcquireLatestFrame(out GpuFrameLease lease) =>
            inner.TryAcquireLatestFrame(out lease);
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

internal sealed class RecordingRenderOutputSink : IRenderOutputSink
{
    private readonly Func<bool>? _waitBeforeDispose;

    public RecordingRenderOutputSink(
        RenderOutputTarget target,
        string name,
        Func<bool>? waitBeforeDispose = null)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Name = name;
        _waitBeforeDispose = waitBeforeDispose;
    }

    public string Name { get; }

    public RenderOutputTarget Target { get; }

    public bool ThrowOnCreateBinding { get; set; }

    public bool ThrowOnDispose { get; set; }

    public int CreateBindingCount => Volatile.Read(ref _createBindingCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    private int _createBindingCount;
    private int _disposeCount;

    public RenderOutputBindingSnapshot CreateBinding(
        RenderOutputId outputId,
        FrameSize surfaceSize,
        long bindingVersion)
    {
        Interlocked.Increment(ref _createBindingCount);

        if (ThrowOnCreateBinding)
            throw new InvalidOperationException($"Sink '{Name}' failed to create binding.");

        return new()
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
    }

    public ValueTask DisposeAsync()
    {
        if (_waitBeforeDispose is not null && !_waitBeforeDispose())
            throw new TimeoutException($"Sink '{Name}' was disposed before the expected render command was observed.");

        Interlocked.Increment(ref _disposeCount);

        if (ThrowOnDispose)
            throw new InvalidOperationException($"Sink '{Name}' failed to dispose.");

        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingRenderOutputSinkFactory : IRenderOutputSinkFactory
{
    private readonly Queue<RecordingRenderOutputSink> _sinks = new();

    public bool ThrowOnCreateSink { get; set; }

    public IReadOnlyList<RecordingRenderOutputSink> CreatedSinks => _createdSinks;

    private readonly List<RecordingRenderOutputSink> _createdSinks = [];

    public void Enqueue(RecordingRenderOutputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sinks.Enqueue(sink);
    }

    public bool CanCreate(RenderOutputTypeId typeId) =>
        typeId == RenderOutputTypes.PreviewWindow || typeId == RenderOutputTypes.Offscreen;

    public IRenderOutputSink CreateSink(RenderOutputTarget target)
    {
        if (ThrowOnCreateSink)
            throw new InvalidOperationException("Configured sink factory failure.");

        var sink = _sinks.Count > 0
            ? _sinks.Dequeue()
            : new RecordingRenderOutputSink(target, $"sink-{_createdSinks.Count + 1}");

        _createdSinks.Add(sink);
        return sink;
    }
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
