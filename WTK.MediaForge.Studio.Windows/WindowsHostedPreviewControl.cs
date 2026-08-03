using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Windows;

namespace WTK.MediaForge.Studio.Windows;

/// <summary>
/// Native Avalonia child window used exclusively as the Windows GPU preview host.
/// The HWND never crosses this platform assembly's public boundary.
/// </summary>
public sealed partial class WindowsHostedPreviewControl : NativeControlHost, IAsyncDisposable
{
    private readonly WindowsHostedPreviewSurface _surface = new();
    private readonly Func<MediaForgeEngine?> _engineProvider;
    private MediaForgeEngine? _observedEngine;
    private RenderOutputId? _attachedOutputId;
    private int _disposed;

    /// <summary>Gets the logical hosted-preview surface owned by this native host.</summary>
    public WindowsHostedPreviewSurface Surface => _surface;

    internal void NotifyEngineCreated(MediaForgeEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (ReferenceEquals(_observedEngine, engine))
            return;
        if (_observedEngine is not null)
            _observedEngine.StateChanged -= OnEngineStateChanged;
        _observedEngine = engine;
        engine.StateChanged += OnEngineStateChanged;
        _ = TryAttachAsync();
    }

    /// <summary>Creates a Windows native host using the platform runtime engine provider.</summary>
    public WindowsHostedPreviewControl(Func<MediaForgeEngine?> engineProvider)
    {
        _engineProvider = engineProvider ?? throw new ArgumentNullException(nameof(engineProvider));
        SizeChanged += OnSizeChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = CreateWindowExW(
            0, "STATIC", string.Empty, WsChild | WsVisible | WsClipSiblings,
            0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (handle == 0)
            throw new InvalidOperationException("Unable to create the Windows hosted-preview child window.");

        _surface.SetNativeWindowHandle(handle);
        PublishHostMetrics();
        _ = TryAttachAsync();
        return new PlatformHandle(handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _surface.ClearNativeWindowHandle();
        if (control.Handle != 0)
            DestroyWindow(control.Handle);
        base.DestroyNativeControlCore(control);
    }

    /// <summary>Closes the hosted surface after its engine attachment has been removed.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        SizeChanged -= OnSizeChanged;
        if (_observedEngine is not null)
            _observedEngine.StateChanged -= OnEngineStateChanged;
        await DetachAsync().ConfigureAwait(false);
        await _surface.CloseAsync(new HostedPreviewCloseRequest(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => PublishHostMetrics();

    private void OnEngineStateChanged(object? sender, MediaForgeEngineStateChangedEventArgs e)
    {
        if (e.NewState is MediaForgeEngineState.Loaded or MediaForgeEngineState.Running)
            _ = TryAttachAsync();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        PublishHostMetrics();
        _ = TryAttachAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _ = DetachAsync();
        _surface.ClearNativeWindowHandle();
    }

    private async Task TryAttachAsync()
    {
        if (_attachedOutputId is not null || _surface.State == HostedPreviewSurfaceState.Closed)
            return;

        var engine = _engineProvider();
        var output = engine?.CurrentProject?.Outputs.FirstOrDefault(candidate =>
            candidate.Enabled && candidate.TypeId == RenderOutputTypes.PreviewWindow);
        if (engine is null || output is null)
            return;

        try
        {
            await engine.AttachHostedPreviewAsync(output.Id, _surface, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            _attachedOutputId = output.Id;
            PublishHostMetrics();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"Hosted preview attachment failed: {exception}");
        }
    }

    private async Task DetachAsync()
    {
        if (_attachedOutputId is not { } outputId)
            return;
        _attachedOutputId = null;
        var engine = _engineProvider();
        if (engine is null || engine.State == MediaForgeEngineState.Disposed)
            return;
        try { await engine.DetachHostedPreviewAsync(outputId, _surface, TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception exception) { System.Diagnostics.Trace.TraceError($"Hosted preview detachment failed: {exception}"); }
    }

    private void PublishHostMetrics()
    {
        if (_surface.State != HostedPreviewSurfaceState.Attached || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var scaling = (VisualRoot as TopLevel)?.RenderScaling ?? 1d;
        var width = checked((uint)Math.Max(1, Math.Round(Bounds.Width * scaling)));
        var height = checked((uint)Math.Max(1, Math.Round(Bounds.Height * scaling)));
        _ = _surface.ResizeAsync(new HostedPreviewResizeRequest(
            new FrameSize(width, height),
            new HostedPreviewDpiScale((float)scaling, (float)scaling), TimeSpan.FromSeconds(2)));
    }

    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
}
