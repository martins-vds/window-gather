namespace WindowGather;

internal sealed class ShortcutForm : Form
{
    private readonly ShortcutEditor gather;
    private readonly ShortcutEditor restore;
    private readonly Label error = new() { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.Firebrick };

    internal ShortcutForm(ShortcutSettings settings, Action<ShortcutSettings> save)
    {
        Text = "Keyboard shortcuts";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(600, 560);
        MinimumSize = new Size(616, 599);
        BackColor = SystemColors.Window;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20),
            ColumnCount = 1, RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 6; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "Choose a modifier and key for each action.\nChanges apply immediately and are kept when you restart.",
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16)
        }, 0, 0);
        gather = new ShortcutEditor("Gather onto the display under your pointer", settings.Gather);
        restore = new ShortcutEditor("Restore borrowed windows", settings.Restore);
        layout.Controls.Add(gather, 0, 1);
        layout.Controls.Add(restore, 0, 2);
        var defaults = new Button { Text = "Use defaults", AutoSize = true };
        defaults.Click += (_, _) =>
        {
            gather.SetShortcut(ShortcutSettings.Defaults.Gather);
            restore.SetShortcut(ShortcutSettings.Defaults.Restore);
            error.Text = "";
        };
        layout.Controls.Add(defaults, 0, 3);
        layout.Controls.Add(error, 0, 4);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        var apply = new Button { Text = "&Save shortcuts", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        apply.Click += (_, _) =>
        {
            try
            {
                save(new ShortcutSettings(gather.GetShortcut(), restore.GetShortcut()));
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                error.Text = exception.Message + "\nButtons and tray actions remain available.";
            }
        };
        buttons.Controls.AddRange([apply, cancel]);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);
        AcceptButton = apply;
        CancelButton = cancel;
    }
}

internal sealed class ShortcutEditor : TableLayoutPanel
{
    private readonly CheckBox ctrl = new() { Text = "Ctrl", AutoSize = true };
    private readonly CheckBox alt = new() { Text = "Alt", AutoSize = true };
    private readonly CheckBox shift = new() { Text = "Shift", AutoSize = true };
    private readonly CheckBox win = new() { Text = "Win", AutoSize = true };
    private readonly ComboBox key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };

    internal ShortcutEditor(string action, Shortcut shortcut)
    {
        AutoSize = true;
        Dock = DockStyle.Fill;
        ColumnCount = 1;
        RowCount = 2;
        Margin = new Padding(0, 0, 0, 14);
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(new Label { Text = action, AutoSize = true, Dock = DockStyle.Fill }, 0, 0);
        var inputs = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        key.AccessibleName = action + " key";
        foreach (Keys supported in Shortcut.SupportedKeys) key.Items.Add(supported);
        key.Format += (_, e) =>
        {
            if (e.ListItem is Keys number && number >= Keys.D0 && number <= Keys.D9)
                e.Value = ((int)number - (int)Keys.D0).ToString();
        };
        key.FormattingEnabled = true;
        inputs.Controls.AddRange([ctrl, alt, shift, win, key]);
        Controls.Add(inputs, 0, 1);
        SetShortcut(shortcut);
    }

    internal Shortcut GetShortcut() => new(
        (ctrl.Checked ? 2u : 0) | (alt.Checked ? 1u : 0) |
        (shift.Checked ? 4u : 0) | (win.Checked ? 8u : 0), (Keys)key.SelectedItem!);

    internal void SetShortcut(Shortcut shortcut)
    {
        ctrl.Checked = (shortcut.Modifiers & 2) != 0;
        alt.Checked = (shortcut.Modifiers & 1) != 0;
        shift.Checked = (shortcut.Modifiers & 4) != 0;
        win.Checked = (shortcut.Modifiers & 8) != 0;
        key.SelectedItem = shortcut.Key;
    }
}
