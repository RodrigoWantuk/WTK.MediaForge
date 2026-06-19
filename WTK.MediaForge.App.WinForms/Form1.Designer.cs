namespace WMF.Testing
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            label1 = new Label();
            cmbMonitors = new ComboBox();
            lblStatus = new Label();
            btnStart = new Button();
            btnStop = new Button();
            timerCapture = new System.Windows.Forms.Timer(components);
            pnlPreview = new Panel();
            label2 = new Label();
            txtOverlay = new TextBox();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(31, 22);
            label1.Name = "label1";
            label1.Size = new Size(48, 15);
            label1.TabIndex = 0;
            label1.Text = "Display:";
            // 
            // cmbMonitors
            // 
            cmbMonitors.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cmbMonitors.FormattingEnabled = true;
            cmbMonitors.Location = new Point(111, 19);
            cmbMonitors.Name = "cmbMonitors";
            cmbMonitors.Size = new Size(602, 23);
            cmbMonitors.TabIndex = 1;
            // 
            // lblStatus
            // 
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblStatus.Location = new Point(31, 45);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(682, 32);
            lblStatus.TabIndex = 2;
            // 
            // btnStart
            // 
            btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnStart.Location = new Point(638, 80);
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(75, 23);
            btnStart.TabIndex = 3;
            btnStart.Text = "START";
            btnStart.UseVisualStyleBackColor = true;
            btnStart.Click += btnStart_Click;
            // 
            // btnStop
            // 
            btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnStop.Location = new Point(557, 80);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(75, 23);
            btnStop.TabIndex = 4;
            btnStop.Text = "STOP";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            // 
            // timerCapture
            // 
            timerCapture.Interval = 1;
            timerCapture.Tick += timerCapture_Tick;
            // 
            // pnlPreview
            // 
            pnlPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlPreview.BackColor = Color.Black;
            pnlPreview.BorderStyle = BorderStyle.FixedSingle;
            pnlPreview.Location = new Point(31, 142);
            pnlPreview.Name = "pnlPreview";
            pnlPreview.Size = new Size(682, 364);
            pnlPreview.TabIndex = 5;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(31, 114);
            label2.Name = "label2";
            label2.Size = new Size(74, 15);
            label2.TabIndex = 6;
            label2.Text = "Overlay Text:";
            // 
            // txtOverlay
            // 
            txtOverlay.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtOverlay.Location = new Point(111, 111);
            txtOverlay.Name = "txtOverlay";
            txtOverlay.Size = new Size(602, 23);
            txtOverlay.TabIndex = 7;
            txtOverlay.TextChanged += txtOverlay_TextChanged;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(764, 518);
            Controls.Add(txtOverlay);
            Controls.Add(label2);
            Controls.Add(pnlPreview);
            Controls.Add(btnStop);
            Controls.Add(btnStart);
            Controls.Add(lblStatus);
            Controls.Add(cmbMonitors);
            Controls.Add(label1);
            Name = "Form1";
            Text = "WTK MediaForge - Desktop Capture POC";
            FormClosing += Form1_FormClosing;
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private ComboBox cmbMonitors;
        private Label lblStatus;
        private Button btnStart;
        private Button btnStop;
        private System.Windows.Forms.Timer timerCapture;
        private Panel pnlPreview;
        private Label label2;
        private TextBox txtOverlay;
    }
}
