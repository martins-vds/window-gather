#if UI_PARITY_TEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WindowGather.Presentation;

namespace WindowGather.WinUI;

internal static class UiParityTests
{
    public static async Task RunAsync(bool interactivePreview = false)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "ui-test-state-" + Guid.NewGuid().ToString("N"));
        var desktop = new TestDesktop();
        var store = new SessionStore(directory);
        var coordinator = new OperationCoordinator(new GatherEngine(desktop, store, desktop, desktop, desktop,
            new GatherPreferencesStore(directory)));
        var shortcuts = new ShortcutController(new TestHotkeys(), new ShortcutStore(directory));
        var dispatcher = new UiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        var startupRegistration = new TestStartupRegistration();
        var startup = new StartupController(startupRegistration);
        startup.Refresh();
        using var model = new MainViewModel(coordinator, shortcuts, dispatcher, startup: startup);
        model.UpdateShortcuts(shortcuts.Activate(ShortcutSettings.Defaults));
        var window = new MainWindow(model, () => shortcuts.Settings);
        Check(!window.AppWindow.IsVisible, "Constructing the tray-launch window does not show or activate it.");
        window.ShowWindow();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-parity-results.txt"), "RUNNING");
        try
        {
            if (interactivePreview)
            {
                window.Title += " - UI preview (simulated windows)";
                var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.ExitRequested += () => exited.TrySetResult();
                model.AttentionRequested += window.ShowWindow;
                model.IdentifyRequested += () => model.ReportProblem("This UI preview uses simulated displays, not your physical monitors.");
                CleanupTestState(directory);
                Check(!Directory.Exists(directory), "Unused preview storage needs no cleanup.");
                await model.ExecuteAsync(OperationKind.Refresh);
                await exited.Task;
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-parity-results.txt"),
                    "PREVIEW CLOSED. Simulated windows; automated parity results are in the separate test build.");
                return;
            }
            await model.ExecuteAsync(OperationKind.Refresh);
            await SettleAsync();
            var root = (Grid)window.Content;
            var picker = (ComboBox)root.FindName("DisplayPicker");
            var preview = (Canvas)root.FindName("Preview");
            var details = (TextBox)root.FindName("Details");
            var startupOption = (CheckBox)root.FindName("StartupOption");
            Check(startupOption.IsChecked == false && startupOption.IsEnabled &&
                startupRegistration.Writes == 0, "Startup defaults off without registering the test executable.");
            await model.SaveStartupAsync(true);
            await SettleAsync();
            Check(startupOption.IsChecked == true && !model.HasDetails, "Startup binding reflects verified enable.");
            startupRegistration.Fail = true;
            await model.SaveStartupAsync(false);
            await SettleAsync();
            Check(startupOption.IsChecked == true && model.HasDetails, "Failed startup save preserves the enabled control.");
            startupRegistration.Fail = false;
            await model.SaveStartupAsync(false);
            await SettleAsync();
            Check(startupOption.IsChecked == false && !model.HasDetails, "Startup disable clears only the simulated registration.");
            Check(preview.Children.Count == 2 && picker.SelectedItem is Display, "Preview and dropdown populated.");
            Check(((CheckBox)root.FindName("PrimaryOption")).IsChecked == false &&
                !((CheckBox)root.FindName("FollowOption")).IsEnabled, "Primary options default off with dependent control disabled.");
            await CaptureAsync(root, "primary-ui-wide.png");
            model.SelectedDisplay = desktop.Displays[1];
            await SettleAsync();
            Check(Equals(picker.SelectedItem, model.SelectedDisplay), "Preview selection and dropdown synchronized.");
            Check(Equals(preview.Children.OfType<Button>().Single(b => b.BorderThickness.Left == 3).Tag, model.SelectedDisplay),
                "Preview highlights the same destination.");
            var gather = Descendants(root).OfType<Button>().Single(b => Equals(b.Content, "Gather to selected display"));
            var restore = Descendants(root).OfType<Button>().Single(b => Equals(b.Content, "Restore session"));
            Check(gather.IsEnabled && !restore.IsEnabled, "Real command bindings initially enabled.");
            await model.GatherCommand.ExecuteAsync(null);
            await SettleAsync();
            Check(!gather.IsEnabled && restore.IsEnabled && !picker.IsEnabled, "Real command bindings and recovery selection lock.");
            Check(startupOption.IsEnabled, "Startup remains independently editable during saved recovery.");
            Check(preview.Children.OfType<Button>().All(b => !b.IsEnabled), "Preview locks during recovery.");
            Check(store.Load()!.Windows.Count == 1 && desktop.ResidentUnchanged, "Persistent recovery excludes residents.");
            desktop.FailRestore = true;
            await model.RestoreCommand.ExecuteAsync(null);
            await SettleAsync();
            Check(model.HasRecovery && details.Visibility == Visibility.Visible && details.Text.Length > 0,
                "Partial restore shows only actual errors and retains recovery.");
            var footer = (StackPanel)root.FindName("Footer");
            Windows.Foundation.Rect footerBounds = footer.TransformToVisual(root)
                .TransformBounds(new(0, 0, footer.ActualWidth, footer.ActualHeight));
            Check(footerBounds.Bottom <= root.ActualHeight + 1, "Shortcut footer remains pinned with errors.");
            desktop.FailRestore = false;
            await model.RestoreCommand.ExecuteAsync(null);
            await SettleAsync();
            Check(!model.HasRecovery && details.Visibility == Visibility.Collapsed && gather.IsEnabled && !restore.IsEnabled,
                "Successful restore resets commands and hides error details.");
            Check(Descendants(root).OfType<TextBlock>().Count(t => t.Text.StartsWith("Restored ")) == 1,
                "Exactly one success summary.");
            await model.GatherCommand.ExecuteAsync(null);
            Display destination = desktop.Displays[1];
            desktop.Displays.Remove(destination);
            desktop.FailRestore = true;
            int attention = 0;
            model.AttentionRequested += () => attention++;
            await model.NotifyTopologyChangedAsync();
            await SettleAsync();
            var recoveryWarning = (InfoBar)root.FindName("RecoveryWarning");
            Check(recoveryWarning.IsOpen && recoveryWarning.Message.Contains("Reconnection will not move") &&
                store.Load()!.AutomaticReturnAttempted && details.Visibility == Visibility.Visible,
                "Destination loss keeps failed returns durable and visibly pending.");
            Check(attention == 0 && model.Summary.StartsWith("Destination disconnected."),
                "Automatic return shows one summary without requesting foreground activation.");
            desktop.Displays.Add(destination);
            desktop.FailRestore = false;
            int returned = desktop.RestoreCount;
            await model.NotifyTopologyChangedAsync();
            await SettleAsync();
            Check(desktop.RestoreCount == returned && recoveryWarning.IsOpen && restore.IsEnabled,
                "Reconnect enables explicit return without automatic window movement.");
            model.ShortcutWarning = "Simulated shortcut-registration warning.";
            double scale = NativeMessage.GetWindowDpi(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;
            window.AppWindow.Resize(new((int)(520 * scale), (int)(650 * scale)));
            await SettleAsync();
            Check(Grid.GetRow(restore) == 1 && Grid.GetColumnSpan(gather) == 2, "Compact window stacks primary actions.");
            foreach (string name in new[] { "ShortcutsButton", "ForgetButton", "ExitButton" })
            {
                var button = (Button)root.FindName(name);
                var bounds = button.TransformToVisual(root).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                Check(bounds.Left >= 0 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1,
                    "Compact footer keeps " + name + " visible.");
            }
            await CaptureAsync(root, "primary-ui-compact.png");
            await model.RestoreCommand.ExecuteAsync(null);
            await SettleAsync();
            Check(!recoveryWarning.IsOpen && !model.HasDetails && desktop.ResidentUnchanged,
                "Explicit return clears pending warning and leaves residents unchanged.");
            model.ShortcutWarning = "";
            root.RequestedTheme = ElementTheme.Dark;
            await SettleAsync();
            Check(root.ActualTheme == ElementTheme.Dark && preview.Children.OfType<Button>().All(b => b.Style is not null),
                "Dark theme retains native monitor styles.");
            root.RequestedTheme = ElementTheme.Light;
            window.AppWindow.Resize(new((int)(760 * scale), (int)(780 * scale)));
            await SettleAsync();
            Check(root.ActualTheme == ElementTheme.Light && Grid.GetRow(restore) == 0 && Grid.GetColumn(gather) == 0,
                "Wide light window restores side-by-side primary actions.");
            await model.SaveGatherOptionsAsync(new(true, true));
            await SettleAsync();
            Check(model.MakeDestinationPrimary && model.FollowNewWindows && model.CanFollowOptions &&
                ((CheckBox)root.FindName("PrimaryOption")).IsChecked == true &&
                ((CheckBox)root.FindName("FollowOption")).IsChecked == true,
                "Primary options persist and enable dependent following option.");
            await model.GatherCommand.ExecuteAsync(null);
            await SettleAsync();
            Check(!((CheckBox)root.FindName("PrimaryOption")).IsEnabled && !model.CanFollowOptions &&
                model.HasPrimaryRecovery && model.HasRecoveryWarning, "Primary session locks options and shows recovery.");
            var restarted = new GatherEngine(desktop, new SessionStore(directory), desktop, desktop);
            desktop.PrimaryId = "test-a";
            Check(restarted.Session is not null && restarted.Restore().Completed == 1, "Recovery is readable across startup.");
            string reference = model.ShortcutReference;
            NativeMessage.RequestSystemClose(WinRT.Interop.WindowNative.GetWindowHandle(window));
            await SettleAsync();
            Check(!window.AppWindow.IsVisible, "Closing hides instead of terminating.");
            window.ShowWindow();
            await SettleAsync();
            Check(window.AppWindow.IsVisible && model.ShortcutReference == reference, "Reopen preserves state.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-parity-results.txt"),
                "PASS WinUI XAML selection, command invalidation, recovery lock, persistent recovery, resident exclusion, " +
                "partial restore, destination disconnect, durable pending warning, reconnect without movement, " +
                "error visibility, pinned footer with simultaneous warnings, one summary, compact layout, light/dark themes, " +
                "default-off primary options, dependent binding, durable primary session, recovery option lock, restart, " +
                "startup option enable/disable/failure, initially hidden window, close-to-hide and reopen.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-parity-results.txt"), error.ToString());
            Environment.ExitCode = 1;
            throw;
        }
        finally
        {
            shortcuts.Release();
            CleanupTestState(directory);
            window.ExitWindow();
        }
    }

    private static void CleanupTestState(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string name in new[] { "session.json", "session.json.tmp", "shortcuts.json", "shortcuts.json.tmp",
            "gather-options.json", "gather-options.json.tmp" })
            File.Delete(Path.Combine(directory, name));
        Directory.Delete(directory);
    }

    private static async Task SettleAsync() => await Task.Delay(150);
    private static async Task CaptureAsync(FrameworkElement root, string name)
    {
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        var buffer = await bitmap.GetPixelsAsync();
        using var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
        byte[] pixels = new byte[buffer.Length];
        reader.ReadBytes(pixels);
        string path = Path.Combine(AppContext.BaseDirectory, name);
        File.WriteAllBytes(path, []);
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
            Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight,
            96, 96, pixels);
        await encoder.FlushAsync();
    }
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException("UI parity failed: " + message);
    }
    private sealed class TestStartupRegistration : IStartupRegistration
    {
        private bool enabled;
        public bool Fail;
        public int Writes;
        public StartupState Read() => new(enabled);
        public void SetEnabled(bool value)
        {
            if (Fail) throw new IOException("Simulated startup registration denied.");
            enabled = value;
            Writes++;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class TestHotkeys : IHotkeyBackend
    {
        public void Register(int id, Shortcut shortcut) { }
        public void Unregister(int id) { }
    }

    private sealed class TestDesktop : IDesktop, IPrimaryDisplay, IWindowInspector, IWindowTracker
    {
        public string PrimaryId { get; set; } = "test-a";
        public string GetPrimaryDisplayId() => PrimaryId;
        public void SetPrimaryDisplay(string displayId) => PrimaryId = displayId;
        public SavedWindow Inspect(SavedWindow window, string token) => windows[window.Handle];
        public event Action<SavedWindow>? WindowOpened { add { } remove { } }
        public event Action<string>? Problem { add { } remove { } }
        public void Start(string token, Display target) { }
        public void Stop() { }
        public List<Display> Displays { get; } = [
            new("test-a", @"\\.\DISPLAY1", "Desk", new(-1920, 0, 0, 1080), new(-1920, 0, 0, 1040)),
            new("test-b", @"\\.\DISPLAY4", "Destination", new(0, 0, 1920, 1080), new(0, 40, 1920, 1080))];
        private readonly Dictionary<long, string> markers = [];
        private readonly Dictionary<long, SavedWindow> windows;
        private readonly SavedWindow resident;
        public TestDesktop()
        {
            var placement = new Placement(0, 1, new(-1, -1), new(-1, -1), new(-1800, 100, -1300, 600));
            resident = new(2, 123, 1000, "ResidentWindow", placement, Displays[1]);
            windows = new()
            {
                [1] = new(1, 123, 1000, "UiParityWindow", placement, Displays[0]), [2] = resident
            };
        }
        public bool FailRestore { get; set; }
        public int RestoreCount { get; private set; }
        public bool ResidentUnchanged => windows[2] == resident;
        public IReadOnlyList<Display> GetDisplays() => Displays;
        public Display GetPointerDisplay() => Displays[1];
        public WindowScan CaptureWindows(string token, Display target)
        {
            foreach (SavedWindow window in windows.Values) markers[window.Handle] = token;
            return new(windows.Values.ToList(), []);
        }
        public bool Matches(SavedWindow window, string token) => markers.GetValueOrDefault(window.Handle) == token;
        public void Mark(SavedWindow window, string token) { }
        public void Unmark(SavedWindow window, string token) => markers.Remove(window.Handle);
        public void Gather(SavedWindow window, Display target) => windows[window.Handle] = window with { Origin = target };
        public void Restore(SavedWindow window)
        {
            if (FailRestore) throw new InvalidOperationException("Simulated restoration failure.");
            RestoreCount++;
            windows[window.Handle] = window;
        }
    }
}
#endif
