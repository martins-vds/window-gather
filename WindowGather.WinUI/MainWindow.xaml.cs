using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using WindowGather.Presentation;
using Windows.Graphics;

namespace WindowGather.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly Func<ShortcutSettings> getSettings;
    private bool exiting;
    private bool dialogOpen;
    public MainViewModel ViewModel { get; }
    public event Action? ExitRequested;

    public MainWindow(MainViewModel model, Func<ShortcutSettings> getSettings)
    {
        ViewModel = model;
        this.getSettings = getSettings;
        InitializeComponent();
        Version version = typeof(App).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Application version metadata is missing.");
        Title = $"Window Gather {version.ToString(3)}";
        SystemBackdrop = new MicaBackdrop();
        Root.ActualThemeChanged += (_, _) => UpdateTitleBarTheme();
        UpdateTitleBarTheme();
        double scale = NativeMessage.GetWindowDpi(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min(work.Width, (int)Math.Round(760 * scale)),
            Math.Min(work.Height, (int)Math.Round(780 * scale))));
        AppWindow.Closing += (_, args) =>
        {
            if (!exiting) { args.Cancel = true; AppWindow.Hide(); }
        };
        model.PropertyChanged += ModelChanged;
        model.ShortcutsRequested += async () => await ShowShortcutsAsync();
    }

    public bool NotBusy(bool busy) => !busy;
    public InfoBarSeverity StatusSeverity(bool hasDetails) => hasDetails ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
    public Style RecoveryButtonStyle(bool hasRecovery) =>
        (Style)Microsoft.UI.Xaml.Application.Current.Resources[hasRecovery ? "AccentButtonStyle" : "DefaultButtonStyle"];
    public void ShowWindow() { AppWindow.Show(); Activate(); }
    public void ExitWindow() { exiting = true; ViewModel.PropertyChanged -= ModelChanged; Close(); }

    private void UpdateTitleBarTheme() =>
        AppWindow.TitleBar.PreferredTheme = Root.ActualTheme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;

    private void RootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool wide = Root.ActualWidth >= 640;
        Grid.SetColumnSpan(GatherButton, wide ? 1 : 2);
        Grid.SetRow(RestoreButton, wide ? 0 : 1);
        Grid.SetColumn(RestoreButton, wide ? 1 : 0);
        Grid.SetColumnSpan(RestoreButton, wide ? 1 : 2);
        Grid.SetColumnSpan(ShortcutsButton, wide ? 1 : 3);
        ShortcutsButton.HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        Grid.SetRow(ForgetButton, wide ? 0 : 1);
        Grid.SetColumn(ForgetButton, wide ? 1 : 0);
        Grid.SetRow(ExitButton, wide ? 0 : 1);
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ViewModel.SelectedDisplay) or nameof(ViewModel.CanSelect))
            DrawPreview();
        if (args.PropertyName == nameof(ViewModel.Details) && ViewModel.HasDetails)
            Details.StartBringIntoView();
    }

    private void PreviewSizeChanged(object sender, SizeChangedEventArgs args) => DrawPreview();

    private void DrawPreview()
    {
        Preview.Children.Clear();
        if (ViewModel.Displays.Count == 0 || Preview.ActualWidth <= 20) return;
        int left = ViewModel.Displays.Min(d => d.Bounds.Left);
        int top = ViewModel.Displays.Min(d => d.Bounds.Top);
        int width = ViewModel.Displays.Max(d => d.Bounds.Right) - left;
        int height = ViewModel.Displays.Max(d => d.Bounds.Bottom) - top;
        if (width <= 0 || height <= 0) return;
        double scale = Math.Min((Preview.ActualWidth - 20) / width, (Preview.Height - 20) / height);
        double offsetX = (Preview.ActualWidth - width * scale) / 2;
        double offsetY = (Preview.Height - height * scale) / 2;
        foreach (Display display in ViewModel.Displays)
        {
            bool selected = display.Id == ViewModel.SelectedDisplay?.Id;
            var button = new Button
            {
                Tag = display,
                Width = Math.Max(1, display.Bounds.Width * scale - 4),
                Height = Math.Max(1, display.Bounds.Height * scale - 4),
                MinWidth = 0, MinHeight = 0, Padding = new Thickness(4),
                IsEnabled = ViewModel.CanSelect,
                BorderThickness = new Thickness(selected ? 3 : 1),
                Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources[selected ? "AccentButtonStyle" : "DefaultButtonStyle"]
            };
            var labels = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            labels.Children.Add(MonitorLabel(display.DeviceName.Replace(@"\\.\", ""), 13, button, true));
            if (button.Width >= 95 && button.Height >= 50)
                labels.Children.Add(MonitorLabel($"{display.Bounds.Width} x {display.Bounds.Height}", 12, button, false));
            button.Content = labels;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Select {display}");
            ToolTipService.SetToolTip(button, display.ToString());
            button.Click += (_, _) => { if (ViewModel.CanSelect) ViewModel.SelectedDisplay = display; };
            Canvas.SetLeft(button, offsetX + (display.Bounds.Left - left) * scale + 2);
            Canvas.SetTop(button, offsetY + (display.Bounds.Top - top) * scale + 2);
            Preview.Children.Add(button);
        }
    }

    private static TextBlock MonitorLabel(string text, double size, Button button, bool strong)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = Math.Max(1, button.Width - 14),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        label.SetBinding(TextBlock.ForegroundProperty, new Binding
        {
            Source = button, Path = new PropertyPath(nameof(Button.Foreground)), Mode = BindingMode.OneWay
        });
        return label;
    }

    private async void ForgetClicked(object sender, RoutedEventArgs args) => await ConfirmForgetAsync();
    private void ExitClicked(object sender, RoutedEventArgs args) => ExitRequested?.Invoke();

    public async Task ConfirmForgetAsync()
    {
        if (dialogOpen || !ViewModel.ForgetCommand.CanExecute(null)) return;
        ShowWindow();
        dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = "Forget recovery?",
                Content = "This removes return positions without moving any windows. This cannot be undone.",
                PrimaryButtonText = "Forget recovery", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.ExecuteAsync(OperationKind.Forget);
        }
        finally { dialogOpen = false; }
    }

    private async Task ShowShortcutsAsync()
    {
        if (dialogOpen || ViewModel.IsBusy) return;
        ShowWindow();
        dialogOpen = true;
        try
        {
            ShortcutSettings settings = getSettings();
            var gather = new ShortcutEditorViewModel(settings.Gather);
            var restore = new ShortcutEditorViewModel(settings.Restore);
            var panel = new StackPanel { Spacing = 16 };
            panel.Children.Add(new TextBlock
            {
                Text = "Changes apply immediately and are saved for restart.", TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(Editor("Gather onto the display under your pointer", gather));
            panel.Children.Add(Editor("Restore borrowed windows", restore));
            panel.Children.Add(new InfoBar
            {
                Message = "Windows reserves F12 for debuggers. The legacy Restore default may fail to register; choose another key if needed.",
                IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning
            });
            var defaults = new Button { Content = "Use defaults" };
            defaults.Click += (_, _) =>
            {
                gather.SetShortcut(ShortcutSettings.Defaults.Gather);
                restore.SetShortcut(ShortcutSettings.Defaults.Restore);
            };
            panel.Children.Add(defaults);
            var error = new InfoBar { IsClosable = false, Severity = InfoBarSeverity.Error };
            panel.Children.Add(error);
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = "Keyboard shortcuts",
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = "Save shortcuts", CloseButtonText = "Cancel"
            };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                dialog.IsPrimaryButtonEnabled = false;
                try
                {
                    var replacement = new ShortcutSettings(gather.GetShortcut(), restore.GetShortcut());
                    replacement.Validate();
                    OperationReply reply = await ViewModel.SaveShortcutsAsync(replacement);
                    args.Cancel = !reply.Accepted || reply.Problems.Count > 0;
                    error.Message = string.Join("\n", reply.Problems.Count > 0 ? reply.Problems : new[] { reply.Summary });
                    error.IsOpen = args.Cancel;
                }
                catch (Exception failure) { args.Cancel = true; error.Message = failure.Message; error.IsOpen = true; }
                finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
            };
            await dialog.ShowAsync();
        }
        finally { dialogOpen = false; }
    }

    private static StackPanel Editor(string title, ShortcutEditorViewModel model)
    {
        var panel = new StackPanel { Spacing = 8, DataContext = model };
        panel.Children.Add(new TextBlock
        {
            Text = title, TextWrapping = TextWrapping.Wrap,
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        var modifiers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (string name in new[] { "Ctrl", "Alt", "Shift", "Win" })
        {
            var checkbox = new CheckBox { Content = name, MinWidth = 0 };
            checkbox.SetBinding(CheckBox.IsCheckedProperty, new Binding { Path = new PropertyPath(name), Mode = BindingMode.TwoWay });
            modifiers.Children.Add(checkbox);
        }
        panel.Children.Add(modifiers);
        var key = new ComboBox { ItemsSource = model.Keys, HorizontalAlignment = HorizontalAlignment.Stretch };
        key.SetBinding(ComboBox.SelectedItemProperty, new Binding { Path = new PropertyPath(nameof(model.Key)), Mode = BindingMode.TwoWay });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(key, title + " key");
        panel.Children.Add(key);
        return panel;
    }
}
