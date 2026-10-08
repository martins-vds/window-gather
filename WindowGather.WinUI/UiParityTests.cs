#if UI_PARITY_TEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WindowGather.Presentation;

namespace WindowGather.WinUI;

internal static class UiParityTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "ui-test-state-" + Guid.NewGuid().ToString("N"));
        var desktop = new TestDesktop();
        var store = new SessionStore(directory);
        var coordinator = new OperationCoordinator(new GatherEngine(desktop, store));
        var shortcuts = new ShortcutController(new TestHotkeys(), new ShortcutStore(directory));
        var dispatcher = new UiDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        using var model = new MainViewModel(coordinator, shortcuts, dispatcher);
        model.UpdateShortcuts(shortcuts.Activate(ShortcutSettings.Defaults));
        var window = new MainWindow(model, () => shortcuts.Settings);
        window.ShowWindow();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-parity-results.txt"), "RUNNING");
        try
        {
            await model.ExecuteAsync(OperationKind.Refresh);
            await SettleAsync();
            var root = (Grid)window.Content;
            var picker = (ComboBox)root.FindName("DisplayPicker");
            var preview = (Canvas)root.FindName("Preview");
            var details = (TextBox)root.FindName("Details");
            Check(preview.Children.Count == 2 && picker.SelectedItem is Display, "Preview and dropdown populated.");
            model.SelectedDisplay = desktop.Displays[1];
            await SettleAsync();
            Check(Equals(picker.SelectedItem, model.SelectedDisplay), "Preview selection and dropdown synchronized.");
            Check(preview.Children.OfType<Button>().Single(b => b.BorderThickness.Left == 3).Content?.ToString() == "DISPLAY4",
                "Preview highlights the same destination.");
            var gather = Descendants(root).OfType<Button>().Single(b => Equals(b.Content, "Gather to selected display"));
            var restore = Descendants(root).OfType<Button>().Single(b => Equals(b.Content, "Restore borrowed windows"));
            Check(gather.IsEnabled && !restore.IsEnabled, "Real command bindings initially enabled.");
            await model.GatherCommand.ExecuteAsync(null);
            await SettleAsync();
            Check(!gather.IsEnabled && restore.IsEnabled && !picker.IsEnabled, "Real command bindings and recovery selection lock.");
            Check(preview.Children.OfType<Button>().All(b => !b.IsEnabled), "Preview locks during recovery.");
            Check(store.Load()!.Windows.Count == 1 && desktop.ResidentUnchanged, "Persistent recovery excludes residents.");
            desktop.FailRestore = true;
            await model.RestoreCommand.ExecuteAsync(null);
            await SettleAsync();
            Check(model.HasRecovery && details.Visibility == Visibility.Visible && details.Text.Length > 0,
                "Partial restore shows only actual errors and retains recovery.");
            var footer = root.Children.OfType<StackPanel>().Single();
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
            var restarted = new GatherEngine(desktop, new SessionStore(directory));
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
                "partial restore, error visibility, pinned footer, one summary, restart, close-to-hide and reopen.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-parity-results.txt"), error.ToString());
            Environment.ExitCode = 1;
            throw;
        }
        finally
        {
            window.ExitWindow();
            shortcuts.Release();
            foreach (string name in new[] { "session.json", "session.json.tmp", "shortcuts.json", "shortcuts.json.tmp" })
                File.Delete(Path.Combine(directory, name));
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    private static async Task SettleAsync() => await Task.Delay(150);
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException("UI parity failed: " + message);
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

    private sealed class TestDesktop : IDesktop
    {
        public IReadOnlyList<Display> Displays { get; } = [
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
            windows[window.Handle] = window;
        }
    }
}
#endif
