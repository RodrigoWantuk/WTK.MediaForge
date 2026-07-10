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
    private FrameSize? _uploadedSize;
    private RenderPixelFormat? _uploadedFormat;
    private IGpuFrameHandle? _uploadedHandle;
    private bool _gpuUploaded;
    private bool _disposed;

    public ImageFileSourceRuntime(
        SourceId sourceId,
        string name,
        ImageFileSourceSettings settings,
        AssetManager assetManager,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _sourceId = sourceId;
        _name = name ?? throw new ArgumentNullException(nameof(name));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
        _diagnostics = diagnostics;
    }

    public ImageFileSourceSettings Settings { get; }

    public MediaSourceState State { get; private set; } = MediaSourceState.Stopped;

    public bool IsGpuUploaded => _gpuUploaded;

    public FrameSize? UploadedSize => _uploadedSize;

    public RenderPixelFormat? UploadedPixelFormat => _uploadedFormat;

    public StaticCpuAsset? LoadedAsset => _textureHandle?.Value;

    public void LoadAsset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _textureHandle?.Dispose();
        _textureHandle = _assetManager.LoadTexture(Settings.Path);
        _uploadedSize = null;
        _uploadedFormat = null;
        _uploadedHandle = null;
        _gpuUploaded = false;
    }

    public void MarkGpuUploaded(IGpuFrameHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(handle);

        if (_textureHandle is null)
            throw new InvalidOperationException("Static image asset must be loaded before it can be marked as uploaded.");

        _uploadedSize = _textureHandle.Value.Size;
        _uploadedFormat = _textureHandle.Value.PixelFormat;
        _uploadedHandle = handle;
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
            Backend = _uploadedHandle?.Backend ?? GpuFrameBackend.Unknown,
            Handle = _uploadedHandle,
            FrameNumber = context.FrameNumber,
            Timestamp = new MediaTime((long)(context.PresentationTime.TotalSeconds * 1_000_000_000)),
            LogicalSize = _uploadedSize ?? default,
            TextureSize = _uploadedSize ?? default
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
        _uploadedSize = null;
        _uploadedFormat = null;
        _uploadedHandle = null;
        State = MediaSourceState.Stopped;
    }
}
