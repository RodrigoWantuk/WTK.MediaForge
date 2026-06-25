using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Composition.Engine;
using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Core.Capture;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using WTK.MediaForge.Windows;

namespace WMF.Testing
{
    public partial class Form1 : Form
    {
        private IReadOnlyList<CaptureSourceInfo> _monitors = Array.Empty<CaptureSourceInfo>();
        private MediaForgeEngine? _engine;
        private PreviewPanelSink? _previewSink;
        private RenderOutputId _outputId;

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

            btnStart.Enabled = _monitors.Count > 0;
            btnStop.Enabled = false;

            lblStatus.Text =
                _monitors.Count > 0
                    ? $"Monitors found: {_monitors.Count}. Ready to start GPU preview."
                    : "No monitors found.";
            lblDiagnostics.Text =
                "Experimental GPU preview harness (PreviewPanelSink). Not a final product API.";
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (_engine is not null)
                return;

            if (cmbMonitors.SelectedItem is not CaptureSourceInfo monitor)
                return;

            btnStart.Enabled = false;

            try
            {
                _engine = MediaForgeWindows.CreateEngine();

                var project = MediaForgeProjectBuilder.Create()
                    .Canvas("Main", 1920, 1080, out var main)
                    .DesktopSource(
                        "Desktop",
                        adapterIndex: (int)monitor.AdapterIndex,
                        outputIndex: (int)monitor.OutputIndex,
                        out var desktop)
                    .AddSourceLayer(
                        main,
                        desktop,
                        layer => layer
                            .SetBounds(0, 0, 1920, 1080)
                            .SetFit()
                            .SetLetterboxBlack())
                    .OffscreenOutput("Program", main, 1920, 1080, out var output, output =>
                        output.CanvasLayoutMode = LayoutMode.Stretch)
                    .BuildValidated();

                await _engine.LoadProjectAsync(project);

                _outputId = output.Id;
                // GPU preview experimental — harness only until presenter lifecycle is product-ready.
                _previewSink = new PreviewPanelSink(pnlPreview.Handle);
                _previewSink.NotifyPanelClientSizeChanged(pnlPreview.ClientSize.Width, pnlPreview.ClientSize.Height);
                await _engine.AttachSinkAsync(_outputId, _previewSink);
                await _engine.StartAsync();

                btnStop.Enabled = true;
                lblStatus.Text = $"Preview running on monitor {monitor.OutputName}.";
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Preview failed to start: {ex.Message}";
                await StopCaptureAsync();
            }
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            await StopCaptureAsync();
        }

        private void timerCapture_Tick(object sender, EventArgs e)
        {
            timerCapture.Stop();
        }

        private void pnlPreview_Resize(object? sender, EventArgs e)
        {
            _previewSink?.NotifyPanelClientSizeChanged(pnlPreview.ClientSize.Width, pnlPreview.ClientSize.Height);
        }

        private void txtOverlay_TextChanged(object sender, EventArgs e)
        {
        }

        private async void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            await StopCaptureAsync();
        }

        private async Task StopCaptureAsync()
        {
            timerCapture.Stop();

            if (_engine is not null)
            {
                try
                {
                    if (_previewSink is not null)
                        await _engine.DetachSinkAsync(_outputId, _previewSink.Id);

                    await _engine.StopAsync();
                }
                catch (Exception ex)
                {
                    lblDiagnostics.Text = $"Stop failed: {ex.Message}";
                }
                finally
                {
                    await _engine.DisposeAsync();
                    _engine = null;
                    _previewSink = null;
                }
            }

            btnStart.Enabled = _monitors.Count > 0;
            btnStop.Enabled = false;

            if (_monitors.Count > 0)
                lblStatus.Text = $"Monitors found: {_monitors.Count}. Ready to start GPU preview.";
        }
    }
}
