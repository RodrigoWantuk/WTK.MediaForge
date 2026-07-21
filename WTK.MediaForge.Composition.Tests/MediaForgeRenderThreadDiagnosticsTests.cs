using System.Collections.Immutable;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

[Collection("RenderThread")]
public class MediaForgeRenderThreadDiagnosticsTests
{
    [Fact]
    public void Submit_failure_reports_to_injected_sink()
    {
        var guard = new RenderThreadGuard();
        var sink = new InMemoryDiagnosticsSink();
        var backend = new ThrowingRenderBackend(guard);

        using var renderThread = new MediaForgeRenderThread(backend, guard, diagnostics: sink);
        renderThread.Start();

        renderThread.PublishFrame(CreateEmptySnapshot(version: 1));

        WaitUntil(() => sink.Diagnostics.Count > 0, TimeSpan.FromSeconds(5));

        Assert.Contains(
            sink.Diagnostics,
            diagnostic => diagnostic.Code == "render.submit_failed");
    }

    private static RenderFrameSnapshot CreateEmptySnapshot(long version) =>
        new()
        {
            ProjectStateVersion = version,
            Canvases = ImmutableArray<RenderCanvasSnapshot>.Empty,
            Outputs = ImmutableArray<RenderOutputStateSnapshot>.Empty,
            FrameLeases = ImmutableArray<Core.Gpu.GpuFrameLease>.Empty
        };

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (condition())
                return;

            Thread.Sleep(10);
        }

        if (condition())
            return;

        throw new TimeoutException("Condition was not met before timeout.");
    }

    private sealed class ThrowingRenderBackend : IRenderBackend
    {
        private readonly RenderThreadGuard _threadGuard;

        public ThrowingRenderBackend(RenderThreadGuard threadGuard) =>
            _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));

        public void BindOutput(RenderOutputBindingSnapshot binding) { }

        public void UnbindOutput(RenderOutputId outputId) { }

        public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize) { }

        public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
        {
            _threadGuard.AssertOnRenderThread();
            throw new InvalidOperationException("Simulated submit failure.");
        }

        public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
