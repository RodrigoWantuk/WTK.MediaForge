using System.Diagnostics;
using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Media.Mux;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Outputs.Settings;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media.Encode;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Windows.Media.Proofs;

namespace WTK.MediaForge.Windows.Media.Qualification;

internal sealed record WindowsSustainedMediaQualificationOptions(
    TimeSpan Duration,
    int Width,
    int Height,
    int FramesPerSecond,
    TimeSpan SampleInterval);

internal sealed record WindowsSustainedMediaQualificationResult(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TimeSpan Duration,
    int Width,
    int Height,
    int FramesPerSecond,
    long BaselinePrivateMemoryBytes,
    long PeakPrivateMemoryBytes,
    long PostStopPrivateMemoryBytes,
    int BaselineHandleCount,
    int PeakHandleCount,
    int PostStopHandleCount,
    long Mp4FileBytes,
    int RtmpVideoPacketCount,
    IReadOnlyList<EncodedOutputRuntimeSnapshot> Outputs,
    IReadOnlyList<MediaForgeDiagnostic> Diagnostics);

internal static class WindowsSustainedMediaQualificationRunner
{
    public static async ValueTask<WindowsSustainedMediaQualificationResult> RunAsync(
        WindowsSustainedMediaQualificationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Sustained Windows media qualification requires Windows.");

        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"wtk_mediaforge_sustained_{Guid.NewGuid():N}.mp4");
        var diagnostics = new InMemoryDiagnosticsSink();
        await using var server = new WindowsLocalRtmpProofServer();
        var engine = MediaForgeWindows.CreateEngine(new MediaForgeEngineOptions
        {
            Diagnostics = diagnostics,
            RenderFramesPerSecond = options.FramesPerSecond,
            StartTimeout = TimeSpan.FromSeconds(30),
            CommandTimeout = TimeSpan.FromSeconds(10),
            StopTimeout = TimeSpan.FromSeconds(30),
            SinkStopTimeout = TimeSpan.FromSeconds(15)
        });

        var project = CreateProject(outputPath, server.Url, options, out var recordingOutputId);
        var process = Process.GetCurrentProcess();
        var baselinePrivateMemory = 0L;
        var baselineHandleCount = 0;
        var peakPrivateMemory = 0L;
        var peakHandleCount = 0;
        var postStopPrivateMemory = 0L;
        var postStopHandleCount = 0;
        var startedAt = default(DateTimeOffset);
        IReadOnlyList<EncodedOutputRuntimeSnapshot> outputSnapshots = [];
        Exception? operationFailure = null;
        var engineDisposed = false;

        try
        {
            await engine.LoadProjectAsync(project, cancellationToken).ConfigureAwait(false);
            await engine.StartAsync(cancellationToken).ConfigureAwait(false);

            var warmup = TimeSpan.FromSeconds(Math.Clamp(options.Duration.TotalSeconds / 10d, 1d, 10d));
            await Task.Delay(warmup, cancellationToken).ConfigureAwait(false);
            process.Refresh();
            baselinePrivateMemory = process.PrivateMemorySize64;
            baselineHandleCount = process.HandleCount;
            peakPrivateMemory = baselinePrivateMemory;
            peakHandleCount = baselineHandleCount;
            startedAt = DateTimeOffset.UtcNow;

            var deadline = Stopwatch.GetTimestamp() +
                           (long)(options.Duration.TotalSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(options.SampleInterval, cancellationToken).ConfigureAwait(false);

                process.Refresh();
                peakPrivateMemory = Math.Max(peakPrivateMemory, process.PrivateMemorySize64);
                peakHandleCount = Math.Max(peakHandleCount, process.HandleCount);

                outputSnapshots = engine.GetEncodedOutputRuntimeSnapshots();
                var failed = outputSnapshots.FirstOrDefault(static output =>
                    output.Status is EncodedOutputRuntimeStatus.Failed or EncodedOutputRuntimeStatus.Unavailable);
                if (failed is not null)
                {
                    throw new InvalidOperationException(
                        $"Encoded output {failed.OutputId} failed during sustained qualification: {failed.Reason}");
                }

                if (engine.State != MediaForgeEngineState.Running)
                {
                    throw new InvalidOperationException(
                        $"Engine left Running state during sustained qualification: {engine.State}.");
                }
            }

            outputSnapshots = engine.GetEncodedOutputRuntimeSnapshots();
            if (outputSnapshots.Count != 2)
            {
                throw new InvalidOperationException(
                    $"Shared encoded route expected two logical outputs, but observed {outputSnapshots.Count}.");
            }

            if (outputSnapshots.Select(static output => output.FramesSubmitted).Distinct().Count() != 1 ||
                outputSnapshots.Select(static output => output.PacketsProduced).Distinct().Count() != 1)
            {
                throw new InvalidOperationException(
                    "MP4 and RTMP outputs were not driven by the same render-to-encode route counters.");
            }

            await engine.StopAsync(cancellationToken).ConfigureAwait(false);

            var recording = outputSnapshots.Single(output => output.OutputId == recordingOutputId);
            if (recording.FramesDropped != 0)
            {
                throw new InvalidOperationException(
                    $"MP4 recording dropped {recording.FramesDropped} frame(s) during sustained qualification.");
            }

            if (recording.PacketsWritten <= 0)
                throw new InvalidOperationException("MP4 recording did not write encoded packets.");

            if (server.VideoPacketCount <= 0)
                throw new InvalidOperationException("RTMP route did not publish any H.264 video packets.");

            if (!IsoBmffMp4Writer.HasValidH264BoxStructure(
                    outputPath,
                    new IsoBmffMp4Writer.TrackMetadata(
                        checked((uint)options.Width),
                        checked((uint)options.Height)),
                    checked((int)Math.Min(recording.PacketsWritten, int.MaxValue))))
            {
                throw new InvalidOperationException("Sustained MP4 recording failed final container validation.");
            }

            await engine.DisposeAsync().ConfigureAwait(false);
            engineDisposed = true;

            process.Refresh();
            postStopPrivateMemory = process.PrivateMemorySize64;
            postStopHandleCount = process.HandleCount;

            return new WindowsSustainedMediaQualificationResult(
                startedAt,
                DateTimeOffset.UtcNow,
                options.Duration,
                options.Width,
                options.Height,
                options.FramesPerSecond,
                baselinePrivateMemory,
                peakPrivateMemory,
                postStopPrivateMemory,
                baselineHandleCount,
                peakHandleCount,
                postStopHandleCount,
                new FileInfo(outputPath).Length,
                server.VideoPacketCount,
                outputSnapshots,
                diagnostics.Diagnostics);
        }
        catch (Exception ex)
        {
            operationFailure = ex;
            throw;
        }
        finally
        {
            var cleanupErrors = new List<Exception>();
            if (engine.State == MediaForgeEngineState.Running)
            {
                try
                {
                    await engine.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    cleanupErrors.Add(cleanupException);
                    MediaForgeDiagnostics.Report(
                        diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "qualification.engine_stop_failed",
                        "Engine stop failed during sustained qualification cleanup.",
                        nameof(WindowsSustainedMediaQualificationRunner),
                        cleanupException);
                }
            }

            try
            {
                if (!engineDisposed)
                    await engine.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                cleanupErrors.Add(cleanupException);
            }

            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch (Exception cleanupException)
            {
                cleanupErrors.Add(cleanupException);
            }

            if (cleanupErrors.Count > 0)
            {
                if (operationFailure is not null)
                    cleanupErrors.Insert(0, operationFailure);

                throw new AggregateException(
                    "Sustained media qualification cleanup did not complete.",
                    cleanupErrors);
            }
        }
    }

    private static MediaForgeProject CreateProject(
        string outputPath,
        string rtmpUrl,
        WindowsSustainedMediaQualificationOptions options,
        out RenderOutputId recordingOutputId)
    {
        var size = new FrameSize(checked((uint)options.Width), checked((uint)options.Height));
        var canvas = new MediaForgeCanvas
        {
            Id = CanvasId.New(),
            Name = "Sustained qualification scene",
            Size = size,
            BackgroundColor = new ColorRgba(0.03f, 0.05f, 0.08f, 1f),
            Objects =
            [
                new SolidDrawObject
                {
                    Id = DrawObjectId.New(),
                    Name = "Qualification fill",
                    FillColor = new ColorRgba(0.05f, 0.62f, 0.92f, 1f),
                    Transform = new Transform2D { Size = new CanvasSize(options.Width, options.Height) }
                }
            ]
        };
        var profile = new EncodedVideoProfile
        {
            FramesPerSecond = options.FramesPerSecond,
            BitrateBitsPerSecond = 8_000_000,
            KeyFrameIntervalFrames = options.FramesPerSecond * 2,
            PixelFormat = "NV12",
            H264Profile = H264Profile.High,
            H264Level = H264Level.Level42
        };
        var recording = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Sustained MP4",
            TypeId = RenderOutputTypes.RecordingMp4,
            Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.RecordMp4(outputPath, profile)),
            CanvasId = canvas.Id,
            OutputSize = size
        };
        var rtmp = new MediaForgeRenderOutput
        {
            Id = RenderOutputId.New(),
            Name = "Sustained RTMP",
            TypeId = RenderOutputTypes.StreamingRtmp,
            Settings = RenderOutputSettingsSerializer.ToJson(MediaForgeOutputs.Rtmp(rtmpUrl, "proof", profile)),
            CanvasId = canvas.Id,
            OutputSize = size
        };

        recordingOutputId = recording.Id;
        return new MediaForgeProject
        {
            Canvases = [canvas],
            Outputs = [recording, rtmp]
        };
    }

    private static void Validate(WindowsSustainedMediaQualificationOptions options)
    {
        if (options.Duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Qualification duration must be positive.");
        if (options.Width <= 0 || options.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Qualification dimensions must be positive.");
        if (options.FramesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Qualification frame rate must be positive.");
        if (options.SampleInterval <= TimeSpan.Zero || options.SampleInterval > options.Duration)
            throw new ArgumentOutOfRangeException(nameof(options), "Sample interval must be positive and no longer than the duration.");
    }
}
