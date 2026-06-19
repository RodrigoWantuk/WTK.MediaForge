using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Graphics.D3D11;
using WTK.MediaForge.Graphics.Vulkan;

namespace WMF.Testing
{
    public partial class Form1 : Form
    {
        private IReadOnlyList<CaptureSourceInfo> _monitors = Array.Empty<CaptureSourceInfo>();
        private DesktopDuplicationCaptureSource? _capture;
        private VulkanPreviewRenderer? _vulkanRenderer;
        private CaptureSourceInfo? _activeCaptureSource;

        private long _lastFrameNumber;
        private DateTime _lastFpsTime = DateTime.UtcNow;
        private int _framesInWindow;

        public Form1()
        {
            InitializeComponent();

            pnlPreview.Resize += pnlPreview_Resize;
        }

        public IntPtr PreviewPanelHandle => pnlPreview.Handle;

        public string OverlayText => txtOverlay.Text;

        private void Form1_Load(object sender, EventArgs e)
        {
            _monitors = DesktopMonitorEnumerator.Enumerate();

            cmbMonitors.DataSource = _monitors.ToList();
            cmbMonitors.DisplayMember = nameof(CaptureSourceInfo.OutputName);

            if (_monitors.Count > 0)
                cmbMonitors.SelectedIndex = 0;

            try
            {
                _vulkanRenderer = new VulkanPreviewRenderer();

                string vulkanInfo = _vulkanRenderer.Initialize(
                    pnlPreview.Handle,
                    pnlPreview.ClientSize.Width,
                    pnlPreview.ClientSize.Height);

                _vulkanRenderer.SetOverlayText(OverlayText);

                lblStatus.Text =
                    $"Monitors found: {_monitors.Count} | Vulkan Preview OK: {vulkanInfo}";
            }
            catch (Exception ex)
            {
                lblStatus.Text =
                    $"Monitors found: {_monitors.Count} | Vulkan Preview failed: {ex.Message}";
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (cmbMonitors.SelectedItem is not CaptureSourceInfo selected)
            {
                MessageBox.Show("Select a monitor first.");
                return;
            }

            _vulkanRenderer?.ClearSource();

            _activeCaptureSource = selected;

            _vulkanRenderer?.SetPreviewParams(
                selected.LogicalSize.Width,
                selected.LogicalSize.Height,
                0,
                0,
                selected.Rotation);

            _vulkanRenderer?.SetOverlayText(OverlayText);

            _capture?.Dispose();
            _capture = new DesktopDuplicationCaptureSource(selected);
            _capture.Start();

            _lastFrameNumber = 0;
            _framesInWindow = 0;
            _lastFpsTime = DateTime.UtcNow;

            timerCapture.Start();

            btnStart.Enabled = false;
            btnStop.Enabled = true;

            lblStatus.Text = $"Capturing: {selected.OutputName}";
            UpdateDiagnosticsPanel(null, 0);
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopCapture();
        }

        private void timerCapture_Tick(object sender, EventArgs e)
        {
            if (_capture is null)
                return;

            if (!_capture.TryAcquireNextFrame(out var frame) || frame is null)
                return;

            try
            {
                if (frame.HasSharedHandle)
                {
                    if (_activeCaptureSource is { } source)
                    {
                        _vulkanRenderer?.SetPreviewParams(
                            source.LogicalSize.Width,
                            source.LogicalSize.Height,
                            frame.Size.Width,
                            frame.Size.Height,
                            source.Rotation);
                    }

                    _vulkanRenderer?.SetSourceD3D11SharedTexture(
                        frame.SharedHandle,
                        frame.Size.Width,
                        frame.Size.Height);
                }

                _vulkanRenderer?.DrawFrame();
            }
            catch (Exception ex)
            {
                timerCapture.Stop();
                lblStatus.Text = $"Vulkan draw/import failed: {ex.Message}";
                return;
            }

            _lastFrameNumber = frame.FrameNumber;
            _framesInWindow++;

            var now = DateTime.UtcNow;
            var elapsed = now - _lastFpsTime;

            if (elapsed.TotalSeconds >= 1)
            {
                double fps = _framesInWindow / elapsed.TotalSeconds;

                lblStatus.Text =
                    $"Frame: {_lastFrameNumber} | FPS: {fps:0.0} | " +
                    $"Handle: 0x{frame.SharedHandle.ToInt64():X} | Overlay: \"{txtOverlay.Text}\"";

                UpdateDiagnosticsPanel(frame, fps);

                _framesInWindow = 0;
                _lastFpsTime = now;
            }
        }

        private void UpdateDiagnosticsPanel(D3D11TextureFrame? frame, double fps)
        {
            if (_activeCaptureSource is null)
            {
                lblDiagnostics.Text = string.Empty;
                return;
            }

            if (frame is null)
            {
                lblDiagnostics.Text = CaptureDiagnosticsFormatter.BuildStartup(
                    _activeCaptureSource,
                    _capture?.SessionInfo,
                    _vulkanRenderer?.GetRendererInfo());
                return;
            }

            lblDiagnostics.Text = CaptureDiagnosticsFormatter.Build(
                _activeCaptureSource,
                _capture?.SessionInfo,
                frame.FrameStats,
                frame,
                _vulkanRenderer?.GetRendererInfo(),
                fps);
        }

        private void pnlPreview_Resize(object? sender, EventArgs e)
        {
            if (pnlPreview.ClientSize.Width <= 0 || pnlPreview.ClientSize.Height <= 0)
                return;

            try
            {
                _vulkanRenderer?.Resize(
                    pnlPreview.ClientSize.Width,
                    pnlPreview.ClientSize.Height);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Vulkan resize failed: {ex.Message}";
            }
        }

        private void txtOverlay_TextChanged(object sender, EventArgs e)
        {
            try
            {
                _vulkanRenderer?.SetOverlayText(OverlayText);
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Overlay update failed: {ex.Message}";
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            timerCapture.Stop();

            _vulkanRenderer?.ClearSource();

            _capture?.Dispose();
            _capture = null;
            _activeCaptureSource = null;

            _vulkanRenderer?.Dispose();
            _vulkanRenderer = null;
        }

        private void StopCapture()
        {
            timerCapture.Stop();

            _vulkanRenderer?.ClearSource();

            _capture?.Dispose();
            _capture = null;
            _activeCaptureSource = null;

            btnStart.Enabled = true;
            btnStop.Enabled = false;

            lblStatus.Text = "Stopped.";
        }
    }
}