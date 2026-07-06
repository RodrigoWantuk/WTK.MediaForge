using WTK.MediaForge.Composition.Assets;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using WTK.MediaForge.Core.Time;
using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Composition.Sources;

internal sealed class ImageFileSourceRuntime : IDisposable
{
    private readonly SourceId _sourceId;
    private readonly string _name;
    private readonly AssetManager _assetManager;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private RefCountedAssetHandle<StaticCpuAsset>? _textureHandle;
    private bool _gpuUploaded;
    private bool _disposed;

    public ImageFileSourceRuntime(
        SourceId sourceId,
        string name,
        ImageFileSourceSettings settings,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _sourceId = sourceId;
        _name = name ?? throw new ArgumentNullException(nameof(name));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _assetManager = AssetManager.Shared;
        _diagnostics = diagnostics;
    }

    public ImageFileSourceSettings Settings { get; }

    public MediaSourceState State { get; private set; } = MediaSourceState.Stopped;

    public bool IsGpuUploaded => _gpuUploaded;

    public StaticCpuAsset? LoadedAsset => _textureHandle?.Value;

    public void LoadAsset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _textureHandle?.Dispose();
        _textureHandle = _assetManager.LoadTexture(Settings.Path);
    }

    public void MarkGpuUploaded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gpuUploaded = true;
        _textureHandle?.Dispose();
        _textureHandle = null;
    }

    public bool TryCreateGpuFrameReference(out GpuFrameReference? frame, RenderFrameContext context)
    {
        frame = null;
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_gpuUploaded)
            return false;

        frame = new GpuFrameReference
        {
            SourceId = _sourceId,
            FrameNumber = context.FrameNumber,
            Timestamp = new MediaTime((long)(context.PresentationTime.TotalSeconds * 1_000_000_000)),
            LogicalSize = _textureHandle?.Value.Size ?? default,
            TextureSize = _textureHandle?.Value.Size ?? default
        };

        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (State == MediaSourceState.Running)
            return Task.CompletedTask;

        LoadAsset();
        State = MediaSourceState.Running;

        MediaForgeDiagnostics.Report(
            _diagnostics,
            MediaForgeDiagnosticSeverity.Info,
            "source.static_image_loaded",
            $"Static image '{Settings.Path}' loaded for source '{_name}'.",
            nameof(ImageFileSourceRuntime),
            sourceId: _sourceId.Value,
            sourceName: _name);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        ObjectDisposedException.ThrowIf(_disposed, this);
        State = MediaSourceState.Stopped;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _textureHandle?.Dispose();
        _textureHandle = null;
        State = MediaSourceState.Stopped;
    }
}
