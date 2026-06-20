using WTK.MediaForge.Capture.DesktopDuplication;
using WTK.MediaForge.Core.Capture;

namespace WMF.Testing
{
    public partial class Form1 : Form
    {
        private IReadOnlyList<CaptureSourceInfo> _monitors = Array.Empty<CaptureSourceInfo>();

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

            btnStart.Enabled = false;
            btnStop.Enabled = false;

            lblStatus.Text =
                $"Monitors found: {_monitors.Count} | Capture preview disabled: legacy GPU path is blocked.";
            lblDiagnostics.Text =
                "Preview must run through the hardened render-thread backend before UI capture is enabled.";
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Capture preview is disabled until it runs through the hardened render-thread backend.",
                "WTK MediaForge",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopCapture();
        }

        private void timerCapture_Tick(object sender, EventArgs e)
        {
            timerCapture.Stop();
        }

        private void pnlPreview_Resize(object? sender, EventArgs e)
        {
        }

        private void txtOverlay_TextChanged(object sender, EventArgs e)
        {
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopCapture();
        }

        private void StopCapture()
        {
            timerCapture.Stop();
            btnStart.Enabled = false;
            btnStop.Enabled = false;
            lblStatus.Text =
                $"Monitors found: {_monitors.Count} | Capture preview disabled: legacy GPU path is blocked.";
        }
    }
}
