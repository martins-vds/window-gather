namespace WindowGather;

internal sealed class DisplayIdentifier : IDisposable
{
    private readonly System.Windows.Forms.Timer timer;
    private readonly List<Form> overlays = [];
    internal IReadOnlyList<Form> Overlays => overlays;

    public DisplayIdentifier(int durationMilliseconds = 3000)
    {
        timer = new System.Windows.Forms.Timer { Interval = durationMilliseconds };
        timer.Tick += (_, _) => Dismiss();
    }

    public void ShowDisplays(IReadOnlyList<Display> displays)
    {
        Dismiss();
        try
        {
            foreach (Display display in displays)
            {
                var overlay = new IdentificationOverlay(display);
                overlays.Add(overlay);
                overlay.Show();
            }
            if (overlays.Count > 0) timer.Start();
        }
        catch
        {
            Dismiss();
            throw;
        }
    }

    public void Dismiss()
    {
        timer.Stop();
        foreach (Form overlay in overlays) overlay.Dispose();
        overlays.Clear();
    }

    public void Dispose()
    {
        Dismiss();
        timer.Dispose();
    }

    private sealed class IdentificationOverlay : Form
    {
        private readonly Font captionFont = new("Segoe UI", 32, FontStyle.Bold);
        private readonly Font descriptionFont = new("Segoe UI", 12);

        public IdentificationOverlay(Display display)
        {
            string caption = display.DeviceName.Replace(@"\\.\", "");
            Text = caption;
            AccessibleName = $"Identify {caption}: {display.Name}";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(0, 90, 158);
            ForeColor = Color.White;
            Box work = display.WorkArea;
            Location = new Point(work.Left + work.Width / 2, work.Top + work.Height / 2);
            // Creating the handle on its destination first gives the overlay that monitor's DPI.
            _ = Handle;
            float scale = DeviceDpi / 96f;
            int width = Math.Min((int)Math.Round(360 * scale), work.Width);
            int height = Math.Min((int)Math.Round(160 * scale), work.Height);
            Bounds = new Rectangle(work.Left + (work.Width - width) / 2,
                work.Top + (work.Height - height) / 2, width, height);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.Controls.Add(new Label
            {
                Text = caption, Font = captionFont, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = display.Name, Font = descriptionFont, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true
            }, 0, 1);
            Controls.Add(layout);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return parameters;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { captionFont.Dispose(); descriptionFont.Dispose(); }
        }
    }
}
