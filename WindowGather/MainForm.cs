using System.ComponentModel;

namespace WindowGather;

public sealed class MainForm : Form
{
    private readonly GatherEngine engine;
    private readonly ComboBox picker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button gather = new() { Text = "&Gather to selected display", AutoSize = true, Padding = new(14, 8, 14, 8) };
    private readonly Button restore = new() { Text = "&Restore borrowed windows", AutoSize = true, Padding = new(14, 8, 14, 8) };
    private readonly Button refresh = new() { Text = "Refresh displays", AutoSize = true };
    private readonly Button identify = new() { Text = "&Identify displays", AutoSize = true };
    private readonly DisplayIdentifier identifier = new();
    private readonly Button forget = new() { Text = "Forget recovery...", AutoSize = true };
    private readonly Button shortcuts = new() { Text = "Change &shortcuts...", AutoSize = true };
    private readonly Label shortcutReference = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Label shortcutWarning = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.Firebrick };
    private readonly ShortcutController shortcutController;
    private ShortcutSettings configuredSettings = ShortcutSettings.Defaults;
    private string? settingsLoadError;
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly TextBox details = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Top, BackColor = SystemColors.Window
    };
    private readonly MonitorMap map = new() { Dock = DockStyle.Fill, Height = 140 };
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem trayGather = new("Gather onto display");
    private readonly ToolStripMenuItem trayRestore = new("Restore borrowed windows");
    private bool busy;
    private bool exiting;

    public MainForm(GatherEngine engine) : this(engine, new ShortcutStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowGather")), null) { }

    internal MainForm(GatherEngine engine, IShortcutStore shortcutStore, IHotkeyBackend? backend)
    {
        this.engine = engine;
        shortcutController = new ShortcutController(backend ?? new NativeHotkeyBackend(() => Handle), shortcutStore);
        ShortcutSettings settings;
        try { settings = shortcutStore.Load(); }
        catch (Exception error)
        {
            settings = ShortcutSettings.Defaults;
            settingsLoadError = $"Cannot load saved shortcuts: {error.Message}\nUsing defaults for now. Save shortcuts to repair the settings.";
        }
        configuredSettings = settings;
        Text = "Window Gather";
        Icon = SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(720, 820);
        MinimumSize = new Size(660, 780);
        BackColor = SystemColors.Window;

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 2
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 7,
            AutoScroll = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 7; row++)
            layout.RowStyles.Add(new RowStyle(row is 2 or 6 ? SizeType.Absolute : SizeType.AutoSize, row == 2 ? 150 : 0));
        shell.Controls.Add(layout, 0, 0);
        var shortcutArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty, ColumnCount = 1, RowCount = 3
        };
        shortcutArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 3; row++) shortcutArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(shortcutArea, 0, 1);

        layout.Controls.Add(new Label
        {
            Text = "Bring your windows here", AutoSize = true,
            Font = new Font("Segoe UI", 22, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = "Gather from other displays. Restore only what you borrowed.\n" +
                "Windows already on the destination stay untouched.",
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12)
        }, 0, 1);
        layout.Controls.Add(map, 0, 2);

        var selection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(0, 10, 0, 14)
        };
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selection.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selection.Controls.Add(picker, 0, 0);
        selection.SetColumnSpan(picker, 2);
        var displayActions = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0)
        };
        displayActions.Controls.AddRange([identify, refresh]);
        selection.Controls.Add(displayActions, 0, 1);
        selection.SetColumnSpan(displayActions, 2);
        picker.AccessibleName = "Destination display";
        layout.Controls.Add(selection, 0, 3);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
        gather.BackColor = Color.FromArgb(0, 90, 158);
        gather.ForeColor = Color.White;
        gather.FlatStyle = FlatStyle.Flat;
        gather.FlatAppearance.BorderSize = 0;
        actions.Controls.AddRange([gather, restore]);
        layout.Controls.Add(actions, 0, 4);
        layout.Controls.Add(status, 0, 5);
        details.AccessibleName = "Operation details and errors";
        details.Height = 96;
        details.Visible = false;
        details.Margin = new Padding(0, 10, 0, 14);
        layout.Controls.Add(details, 0, 6);
        shortcutReference.Margin = new Padding(0, 16, 0, 8);
        shortcutArea.Controls.Add(shortcutReference, 0, 0);
        shortcutWarning.Margin = new Padding(0, 0, 0, 8);
        shortcutArea.Controls.Add(shortcutWarning, 0, 1);
        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var exit = new Button { Text = "Exit", AutoSize = true };
        footer.Controls.AddRange([shortcuts, forget, exit]);
        shortcutArea.Controls.Add(footer, 0, 2);
        Controls.Add(shell);

        menu.Items.Add("Open Window Gather", null, (_, _) => ShowWindow());
        menu.Items.Add(trayGather);
        menu.Items.Add(trayRestore);
        menu.Items.Add("Change shortcuts...", null, (_, _) => ChangeShortcuts());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        tray = new NotifyIcon
        {
            Icon = SystemIcons.Application, Text = "Window Gather", Visible = true, ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => ShowWindow();
        gather.Click += async (_, _) =>
        {
            if (picker.SelectedItem is Display display)
                await Execute(() => engine.Gather(display));
        };
        restore.Click += async (_, _) => await Execute(engine.Restore);
        trayRestore.Click += async (_, _) => await Execute(engine.Restore);
        refresh.Click += (_, _) => TryRefresh();
        identify.Click += (_, _) =>
        {
            try { identifier.ShowDisplays(engine.GetDisplays()); }
            catch (Exception error) { ShowDetails($"Cannot identify displays: {error.Message}"); }
        };
        map.DisplaySelected += display =>
        {
            if (!busy && engine.Session is null) picker.SelectedItem = display;
        };
        picker.SelectedIndexChanged += (_, _) =>
        {
            map.SelectedId = (picker.SelectedItem as Display)?.Id;
            map.Invalidate();
        };
        forget.Click += (_, _) => ForgetSession();
        shortcuts.Click += (_, _) => ChangeShortcuts();
        exit.Click += (_, _) => ExitApplication();
        AcceptButton = gather;

        TryRefresh();
        UpdateActions();
        status.Text = engine.Session is null
            ? "Ready. Choose the display you want to work on."
            : $"Recovery available: {engine.Session.Windows.Count} borrowed windows. Click Restore to send them home.";
        UpdateShortcutReference(settings);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var errors = shortcutController.Activate(configuredSettings);
        UpdateShortcutWarnings(errors);
    }

    private void UpdateShortcutReference(ShortcutSettings settings)
    {
        shortcutReference.Text = $"{settings.Gather}  Gather onto the display under your pointer\n" +
            $"{settings.Restore}  Restore borrowed windows\n" +
            "Closing this window keeps the app running in the notification area.";
    }

    private void UpdateShortcutWarnings(IReadOnlyList<string> errors)
    {
        shortcutWarning.Text = string.Join("\n", new[] { settingsLoadError }
            .Where(s => !string.IsNullOrEmpty(s)).Concat(errors));
        if (errors.Count > 0) shortcutWarning.Text += "\nChange shortcuts, or use the buttons or tray menu.";
        shortcutWarning.Visible = shortcutWarning.Text.Length > 0;
    }

    private void ChangeShortcuts()
    {
        if (busy) return;
        using var dialog = new ShortcutForm(shortcutController.Settings, ApplyShortcuts);
        dialog.ShowDialog(this);
    }

    internal void ApplyShortcuts(ShortcutSettings settings)
    {
        try
        {
            shortcutController.Apply(settings);
            settingsLoadError = null;
            UpdateShortcutWarnings([]);
        }
        finally
        {
            configuredSettings = shortcutController.Settings;
            UpdateShortcutReference(configuredSettings);
        }
    }

    private void ShowDetails(string text)
    {
        details.Text = text;
        int height = (int)Math.Round(96 * DeviceDpi / 96f);
        details.Height = height;
        details.MinimumSize = text.Length > 0 ? new Size(0, height) : Size.Empty;
        details.Visible = text.Length > 0;
        if (details.Parent is TableLayoutPanel layout)
        {
            layout.RowStyles[6].Height = text.Length > 0 ? height + details.Margin.Vertical : 0;
            layout.PerformLayout();
            if (text.Length > 0) layout.ScrollControlIntoView(details);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try { shortcutController.Release(); }
        catch (Win32Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312)
        {
            int id = message.WParam.ToInt32();
            if (id == shortcutController.GatherId)
                _ = Execute(() => engine.Gather(engine.GetPointerDisplay()));
            else if (id == shortcutController.RestoreId)
                _ = Execute(engine.Restore);
        }
        base.WndProc(ref message);
    }

    private async Task Execute(Func<OperationResult> operation)
    {
        if (busy) return;
        busy = true;
        UpdateActions();
        status.Text = "Working... Please wait before moving windows.";
        try
        {
            OperationResult result = await Task.Run(operation).ConfigureAwait(false);
            if (IsDisposed || Disposing) return;
            await InvokeAsync(() =>
            {
                status.Text = result.Summary;
                ShowDetails(string.Join("\r\n", result.Problems));
                if (result.Problems.Count > 0) ShowWindow();
                if (!Visible)
                {
                    tray.BalloonTipTitle = "Window Gather";
                    tray.BalloonTipText = result.Summary;
                    tray.ShowBalloonTip(4000);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (IsDisposed || Disposing) return;
            await InvokeAsync(() =>
            {
                status.Text = "The operation could not finish. Recovery data has not been discarded.";
                ShowDetails($"{error.GetType().Name}: {error.Message}");
                ShowWindow();
            }).ConfigureAwait(false);
        }
        finally
        {
            if (!IsDisposed && !Disposing)
                await InvokeAsync(() =>
                {
                    busy = false;
                    UpdateActions();
                }).ConfigureAwait(false);
        }
    }

    private void TryRefresh()
    {
        try
        {
            string? selected = (picker.SelectedItem as Display)?.Id;
            IReadOnlyList<Display> displays = engine.GetDisplays();
            picker.Items.Clear();
            trayGather.DropDownItems.Clear();
            foreach (Display display in displays)
            {
                picker.Items.Add(display);
                var item = new ToolStripMenuItem(display.ToString());
                item.Click += async (_, _) => await Execute(() => engine.Gather(display));
                trayGather.DropDownItems.Add(item);
            }
            if (picker.Items.Count > 0)
                picker.SelectedIndex = Math.Max(0, displays.ToList().FindIndex(d => d.Id == selected));
            map.Displays = displays;
            map.SelectedId = (picker.SelectedItem as Display)?.Id;
            map.Invalidate();
        }
        catch (Exception error)
        {
            ShowDetails($"Cannot refresh displays: {error.Message}");
        }
        UpdateActions();
    }

    private void UpdateActions()
    {
        GatherSession? session = engine.Session;
        bool active = session is not null;
        gather.Enabled = !busy && !active && picker.Items.Count > 0;
        gather.BackColor = gather.Enabled ? Color.FromArgb(0, 90, 158) : SystemColors.Control;
        gather.ForeColor = gather.Enabled ? Color.White : SystemColors.GrayText;
        restore.Enabled = !busy && active;
        trayGather.Enabled = gather.Enabled;
        trayRestore.Enabled = restore.Enabled;
        picker.Enabled = refresh.Enabled = !busy && !active;
        map.Enabled = picker.Enabled;
        identify.Enabled = !busy && picker.Items.Count > 0;
        forget.Enabled = !busy && active;
        shortcuts.Enabled = !busy;
        tray.Text = session is not null ? $"Window Gather - {session.Windows.Count} borrowed windows" : "Window Gather - ready";
    }

    private void ForgetSession()
    {
        if (busy || engine.Session is null) return;
        if (MessageBox.Show(this,
            "Forget the saved return positions? Windows will stay where they are now, and you " +
            "will no longer be able to restore this session.", "Forget recovery",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            engine.Forget();
            status.Text = "Recovery forgotten. Windows were not moved.";
            ShowDetails("");
        }
        catch (Exception error) { ShowDetails($"Could not forget recovery: {error.Message}"); }
        UpdateActions();
    }

    private void ExitApplication()
    {
        if (busy) return;
        if (engine.Session is not null && MessageBox.Show(this,
            "Borrowed windows will stay where they are. Your return positions are saved; " +
            "reopen Window Gather to restore them. Exit now?", "Exit Window Gather",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        exiting = true;
        Close();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { identifier.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class MonitorMap : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<Display> Displays { get; set; } = [];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedId { get; set; }
    public event Action<Display>? DisplaySelected;

    public MonitorMap()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        AccessibleName = "Display arrangement. Click a display to select it.";
        TabStop = false;
    }

    internal IEnumerable<(Display Display, Rectangle Bounds)> GetDisplayRegions()
    {
        if (Displays.Count == 0 || ClientSize.Width <= 20 || ClientSize.Height <= 20) yield break;
        int left = Displays.Min(d => d.Bounds.Left), top = Displays.Min(d => d.Bounds.Top);
        int width = Displays.Max(d => d.Bounds.Right) - left;
        int height = Displays.Max(d => d.Bounds.Bottom) - top;
        if (width <= 0 || height <= 0) yield break;
        float scale = Math.Min((ClientSize.Width - 20f) / width, (ClientSize.Height - 20f) / height);
        float xOffset = (ClientSize.Width - width * scale) / 2;
        float yOffset = (ClientSize.Height - height * scale) / 2;
        foreach (Display display in Displays)
        {
            var rect = Rectangle.Round(new RectangleF(
                xOffset + (display.Bounds.Left - left) * scale,
                yOffset + (display.Bounds.Top - top) * scale,
                display.Bounds.Width * scale, display.Bounds.Height * scale));
            rect.Inflate(-3, -3);
            if (rect.Width > 0 && rect.Height > 0) yield return (display, rect);
        }
    }

    internal Display? HitTestDisplay(Point point) =>
        GetDisplayRegions().Reverse().FirstOrDefault(region => region.Bounds.Contains(point)).Display;

    internal void SelectDisplayAt(Point point)
    {
        if (!Enabled) return;
        Display? display = HitTestDisplay(point);
        if (display is null) return;
        SelectedId = display.Id;
        Invalidate();
        DisplaySelected?.Invoke(display);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) SelectDisplayAt(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = Enabled && HitTestDisplay(e.Location) is not null ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Cursor = Cursors.Default;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (!Enabled) Cursor = Cursors.Default;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        foreach ((Display display, Rectangle rect) in GetDisplayRegions())
        {
            bool selected = display.Id == SelectedId;
            using var brush = new SolidBrush(selected ? Color.FromArgb(0, 90, 158) : Color.FromArgb(235, 239, 243));
            e.Graphics.FillRectangle(brush, rect);
            TextRenderer.DrawText(e.Graphics, display.DeviceName.Replace(@"\\.\", ""), Font, rect,
                selected ? Color.White : Color.FromArgb(38, 45, 52),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
