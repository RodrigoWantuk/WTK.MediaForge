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
using RuntimeRenderOutputSink = WTK.MediaForge.Composition.Runtime.Outputs.IRenderOutputSink;

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

internal sealed class RecoveringRenderBackendFactory : IRenderBackendFactory
{
    private readonly List<IRenderBackend> _createdBackends = [];

    public IReadOnlyList<IRenderBackend> CreatedBackends => _createdBackends;

    public int CreateAttempts => _createdBackends.Count;

    public NullRenderBackend? ReplacementBackend { get; private set; }

    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        if (_createdBackends.Count == 0)
        {
            backend = new SubmitFailingRenderBackend(threadGuard);
        }
        else
        {
            ReplacementBackend = new NullRenderBackend(threadGuard);
            backend = ReplacementBackend;
        }

        _createdBackends.Add(backend);
        return true;
    }
}

internal sealed class SubmitFailingRenderBackend(RenderThreadGuard threadGuard) : IRenderBackend
{
    public bool Disposed { get; private set; }

    public void BindOutput(RenderOutputBindingSnapshot binding) => threadGuard.AssertOnRenderThread();

    public void UnbindOutput(RenderOutputId outputId) => threadGuard.AssertOnRenderThread();

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) =>
        threadGuard.AssertOnRenderThread();

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        threadGuard.AssertOnRenderThread();
        throw new InvalidOperationException("Configured render submission failure.");
    }

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public void Dispose() => Disposed = true;
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

internal sealed class CommandTrackingRenderBackendFactory : IRenderBackendFactory
{
    private readonly bool _throwOnBind;
    private readonly bool _throwOnUnbind;

    public CommandTrackingRenderBackendFactory(
        bool throwOnBind = false,
        bool throwOnUnbind = false)
    {
        _throwOnBind = throwOnBind;
        _throwOnUnbind = throwOnUnbind;
    }

    public CommandTrackingRenderBackend? Backend { get; private set; }

    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        Backend = new CommandTrackingRenderBackend(threadGuard)
        {
            ThrowOnBind = _throwOnBind,
            ThrowOnUnbind = _throwOnUnbind
        };
        backend = Backend;
        return true;
    }
}

internal sealed class CommandTrackingRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;
    private readonly ManualResetEventSlim _bindEntered = new(false);
    private readonly ManualResetEventSlim _releaseBind = new(true);
    private readonly ManualResetEventSlim _unbindEntered = new(false);
    private readonly ManualResetEventSlim _releaseUnbind = new(true);

    public CommandTrackingRenderBackend(RenderThreadGuard threadGuard) =>
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

    public bool ThrowOnBind { get; set; }

    public bool ThrowOnUnbind { get; set; }

    public bool BlockBindUntilReleased { get; set; }

    public bool BlockUnbindUntilReleased { get; set; }

    public int BindCount => Volatile.Read(ref _bindCount);

    public int UnbindCount => Volatile.Read(ref _unbindCount);

    public bool Disposed { get; private set; }

    private int _bindCount;
    private int _unbindCount;

    public void BindOutput(RenderOutputBindingSnapshot binding)
    {
        _threadGuard.AssertOnRenderThread();
        _bindEntered.Set();

        if (BlockBindUntilReleased && !_releaseBind.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Timed out waiting for test to release bind command.");

        if (ThrowOnBind)
            throw new InvalidOperationException("Configured bind command failure.");

        Interlocked.Increment(ref _bindCount);
    }

    public void UnbindOutput(RenderOutputId outputId)
    {
        _threadGuard.AssertOnRenderThread();
        _unbindEntered.Set();

        if (BlockUnbindUntilReleased && !_releaseUnbind.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Timed out waiting for test to release unbind command.");

        if (ThrowOnUnbind)
            throw new InvalidOperationException("Configured unbind command failure.");

        Interlocked.Increment(ref _unbindCount);
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

    public bool WaitForBindEntered(TimeSpan timeout) => _bindEntered.Wait(timeout);

    public void ResetBindRelease()
    {
        _bindEntered.Reset();
        _releaseBind.Reset();
        BlockBindUntilReleased = true;
    }

    public void ReleaseBind() => _releaseBind.Set();

    public bool WaitForUnbindEntered(TimeSpan timeout) => _unbindEntered.Wait(timeout);

    public void ResetUnbindRelease()
    {
        _unbindEntered.Reset();
        _releaseUnbind.Reset();
        BlockUnbindUntilReleased = true;
    }

    public void ReleaseUnbind() => _releaseUnbind.Set();

    public void Dispose()
    {
        Disposed = true;
        _bindEntered.Dispose();
        _releaseBind.Dispose();
        _unbindEntered.Dispose();
        _releaseUnbind.Dispose();
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

internal sealed class BlockingSubmitRenderBackendFactory : IRenderBackendFactory
{
    public BlockingSubmitRenderBackend? Backend { get; private set; }

    public bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        out IRenderBackend? backend)
    {
        Backend = new BlockingSubmitRenderBackend(threadGuard);
        backend = Backend;
        return true;
    }
}

internal sealed class BlockingSubmitRenderBackend : IRenderBackend
{
    private readonly RenderThreadGuard _threadGuard;
    private readonly ManualResetEventSlim _submitEntered = new(false);
    private readonly ManualResetEventSlim _releaseSubmit = new(false);
    private readonly ManualResetEventSlim _submitExited = new(false);

    public BlockingSubmitRenderBackend(RenderThreadGuard threadGuard) =>
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

    public bool Disposed { get; private set; }

    public void BindOutput(RenderOutputBindingSnapshot binding)
    {
        _threadGuard.AssertOnRenderThread();
    }

    public void UnbindOutput(RenderOutputId outputId)
    {
        _threadGuard.AssertOnRenderThread();
    }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize)
    {
        _threadGuard.AssertOnRenderThread();
    }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        _threadGuard.AssertOnRenderThread();
        _submitEntered.Set();
        try
        {
            if (!_releaseSubmit.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Timed out waiting for test to release blocked submit.");

            return new ImmediateRenderFrameSubmission(snapshot);
        }
        finally
        {
            _submitExited.Set();
        }
    }

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public bool WaitForSubmitEntered(TimeSpan timeout) => _submitEntered.Wait(timeout);

    public bool WaitForSubmitExited(TimeSpan timeout) => _submitExited.Wait(timeout);

    public void ReleaseSubmit() => _releaseSubmit.Set();

    public void Dispose()
    {
        Disposed = true;
        _submitEntered.Dispose();
        _releaseSubmit.Dispose();
        _submitExited.Dispose();
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

internal sealed class FakeRenderOutputSink : RuntimeRenderOutputSink
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

internal sealed class HangingStartMediaSourceProviderFactory : IMediaSourceProviderFactory
{
    public HangingStartProvider? Provider { get; private set; }

    public bool CanCreate(MediaSourceTypeId typeId) => true;

    public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
    {
        Provider = new HangingStartProvider(sourceDefinition.Id, sourceDefinition.Name);
        return Provider;
    }

    internal sealed class HangingStartProvider(SourceId id, string name) : IVideoFrameProvider
    {
        private readonly TaskCompletionSource _start = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SourceId Id { get; } = id;

        public string Name { get; } = name;

        public MediaSourceState State { get; private set; } = MediaSourceState.Stopped;

        public Exception? LastError { get; private set; }

        public bool StopCalled { get; private set; }

        public bool StartCancellationObserved { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            State = MediaSourceState.Running;
            cancellationToken.Register(() =>
            {
                StartCancellationObserved = true;
                _start.TrySetCanceled(cancellationToken);
            });
            return _start.Task;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalled = true;
            State = MediaSourceState.Stopped;
            return Task.CompletedTask;
        }

        public bool TryAcquireLatestFrame(out GpuFrameLease lease)
        {
            lease = null!;
            return false;
        }
    }
}

internal sealed class RecordingRenderOutputSink : RuntimeRenderOutputSink
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

    public RuntimeRenderOutputSink CreateSink(RenderOutputTarget target)
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

    public RuntimeRenderOutputSink CreateSink(RenderOutputTarget target) => new FakeRenderOutputSink(target);
}

internal sealed class RejectingRenderOutputSinkFactory : IRenderOutputSinkFactory
{
    public bool CanCreate(RenderOutputTypeId typeId) => false;

    public RuntimeRenderOutputSink CreateSink(RenderOutputTarget target) =>
        throw new InvalidOperationException("Rejecting factory should not create sinks.");
}

internal sealed class RecordingPublicRenderOutputSink : WTK.MediaForge.Composition.Outputs.IRenderOutputSink
{
    private readonly Func<RenderOutputFrameLease, CancellationToken, ValueTask>? _onFrame;

    public RecordingPublicRenderOutputSink(
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<RenderOutputFrameLease, CancellationToken, ValueTask>? onFrame = null)
        : this(RenderOutputSinkId.New(), backpressureMode, onFrame)
    {
    }

    public RecordingPublicRenderOutputSink(
        RenderOutputSinkId id,
        RenderOutputSinkBackpressureMode backpressureMode = RenderOutputSinkBackpressureMode.KeepLatest,
        Func<RenderOutputFrameLease, CancellationToken, ValueTask>? onFrame = null)
    {
        Id = id;
        BackpressureMode = backpressureMode;
        _onFrame = onFrame;
    }

    public RenderOutputSinkId Id { get; }

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

    public RenderOutputSinkBackpressureMode BackpressureMode { get; }

    public int StartCount => Volatile.Read(ref _startCount);

    public int StopCount => Volatile.Read(ref _stopCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public int FrameCount => Volatile.Read(ref _frameCount);

    public List<RenderOutputFrameInfo> Frames { get; } = [];

    public bool ThrowOnStart { get; set; }

    public bool ThrowOnFrame { get; set; }

    private int _startCount;
    private int _stopCount;
    private int _disposeCount;
    private int _frameCount;

    public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _startCount);

        if (ThrowOnStart)
            throw new InvalidOperationException("Configured public sink start failure.");

        return ValueTask.CompletedTask;
    }

    public async ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _frameCount);
        lock (Frames)
            Frames.Add(frame.Info);

        if (ThrowOnFrame)
            throw new InvalidOperationException("Configured public sink frame failure.");

        if (_onFrame is not null)
            await _onFrame(frame, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _stopCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        return ValueTask.CompletedTask;
    }
}

internal sealed class HangingStartPublicRenderOutputSink : WTK.MediaForge.Composition.Outputs.IRenderOutputSink
{
    private readonly TaskCompletionSource _start =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

    public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

    public bool StartCancellationObserved { get; private set; }

    public int StopCount => Volatile.Read(ref _stopCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    private int _stopCount;
    private int _disposeCount;

    public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken)
    {
        cancellationToken.Register(() =>
        {
            StartCancellationObserved = true;
            _start.TrySetCanceled(cancellationToken);
        });

        return new ValueTask(_start.Task);
    }

    public ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _stopCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        return ValueTask.CompletedTask;
    }
}

internal sealed class BlockingPublicRenderOutputSink : WTK.MediaForge.Composition.Outputs.IRenderOutputSink
{
    private readonly TaskCompletionSource _frameEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseFrame =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.Preview;

    public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

    public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public async ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
    {
        _frameEntered.TrySetResult();
        await _releaseFrame.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _releaseFrame.TrySetResult();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _releaseFrame.TrySetResult();
        return ValueTask.CompletedTask;
    }

    public Task WaitForFrameAsync(TimeSpan timeout) =>
        _frameEntered.Task.WaitAsync(timeout);

    public void Release() => _releaseFrame.TrySetResult();
}

internal sealed class HungPublicRenderOutputSink : WTK.MediaForge.Composition.Outputs.IRenderOutputSink
{
    private readonly TaskCompletionSource _frameEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseFrame =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RenderOutputSinkId Id { get; } = RenderOutputSinkId.New();

    public RenderOutputSinkKind Kind => RenderOutputSinkKind.Custom;

    public RenderOutputSinkBackpressureMode BackpressureMode => RenderOutputSinkBackpressureMode.KeepLatest;

    public int StopCount => Volatile.Read(ref _stopCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    private int _stopCount;
    private int _disposeCount;

    public ValueTask StartAsync(RenderOutputSinkContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    public async ValueTask OnFrameAsync(RenderOutputFrameLease frame, CancellationToken cancellationToken)
    {
        _frameEntered.TrySetResult();
        await _releaseFrame.Task.ConfigureAwait(false);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _stopCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        return ValueTask.CompletedTask;
    }

    public Task WaitForFrameAsync(TimeSpan timeout) =>
        _frameEntered.Task.WaitAsync(timeout);

    public void Release() => _releaseFrame.TrySetResult();
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
