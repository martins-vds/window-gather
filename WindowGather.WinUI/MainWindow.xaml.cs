using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
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
        double scale = NativeMessage.GetWindowDpi(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min(work.Width, (int)Math.Round(760 * scale)),
            Math.Min(work.Height, (int)Math.Round(900 * scale))));
        AppWindow.Closing += (_, args) =>
        {
            if (!exiting) { args.Cancel = true; AppWindow.Hide(); }
        };
        model.PropertyChanged += ModelChanged;
        model.ShortcutsRequested += async () => await ShowShortcutsAsync();
    }

    public bool NotBusy(bool busy) => !busy;
    public void ShowWindow() { AppWindow.Show(); Activate(); }
    public void ExitWindow() { exiting = true; ViewModel.PropertyChanged -= ModelChanged; Close(); }

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
            var button = new Button
            {
                Content = display.DeviceName.Replace(@"\\.\", ""),
                Width = Math.Max(1, display.Bounds.Width * scale - 4),
                Height = Math.Max(1, display.Bounds.Height * scale - 4),
                Padding = new Thickness(2), FontSize = 12,
                IsEnabled = ViewModel.CanSelect,
                BorderThickness = new Thickness(display.Id == ViewModel.SelectedDisplay?.Id ? 3 : 1)
            };
            if (display.Id == ViewModel.SelectedDisplay?.Id)
                button.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 90, 158));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Select {display}");
            ToolTipService.SetToolTip(button, display.ToString());
            button.Click += (_, _) => { if (ViewModel.CanSelect) ViewModel.SelectedDisplay = display; };
            Canvas.SetLeft(button, offsetX + (display.Bounds.Left - left) * scale + 2);
            Canvas.SetTop(button, offsetY + (display.Bounds.Top - top) * scale + 2);
            Preview.Children.Add(button);
        }
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
            var panel = new StackPanel { Spacing = 16, MinWidth = 340 };
            panel.Children.Add(new TextBlock
            {
                Text = "Changes apply immediately and are saved for restart.", TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(Editor("Gather onto the display under your pointer", gather));
            panel.Children.Add(Editor("Restore borrowed windows", restore));
            panel.Children.Add(new TextBlock
            {
                Text = "Windows reserves F12 for debuggers. The legacy Restore default may fail to register; choose another key if needed.",
                TextWrapping = TextWrapping.Wrap
            });
            var defaults = new Button { Content = "Use defaults" };
            defaults.Click += (_, _) =>
            {
                gather.SetShortcut(ShortcutSettings.Defaults.Gather);
                restore.SetShortcut(ShortcutSettings.Defaults.Restore);
            };
            panel.Children.Add(defaults);
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
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
                    error.Text = string.Join("\n", reply.Problems.Count > 0 ? reply.Problems : new[] { reply.Summary });
                }
                catch (Exception failure) { args.Cancel = true; error.Text = failure.Message; }
                finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
            };
            await dialog.ShowAsync();
        }
        finally { dialogOpen = false; }
    }

    private static StackPanel Editor(string title, ShortcutEditorViewModel model)
    {
        var panel = new StackPanel { Spacing = 8, DataContext = model };
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
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
