using Microsoft.UI.Xaml;
using WindowGather.Presentation;

namespace WindowGather.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
#if !UI_PARITY_TEST
    private Mutex? mutex;
    private MainWindow? window;
    private WindowsShell? shell;
    private NativeDisplayIdentifier? identifier;
    private ShortcutController? shortcuts;
    private MainViewModel? model;
    private OperationCoordinator? coordinator;
#endif

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            System.Diagnostics.Trace.TraceError(args.Exception.ToString());
            NativeMessage.Show(args.Exception.Message, "Window Gather error");
            args.Handled = true;
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
#if UI_PARITY_TEST
        try
        {
#if UI_PREVIEW
            await UiParityTests.RunAsync(interactivePreview: true);
#else
            await UiParityTests.RunAsync();
#endif
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-parity-results.txt"), error.ToString());
            Environment.ExitCode = 1;
        }
        Exit();
#else
        mutex = new Mutex(true, @"Local\WindowGather.DesktopUtility", out bool first);
        if (!first)
        {
            NativeMessage.Show("Window Gather is already running. Open it from the notification area.", "Window Gather");
            mutex.Dispose();
            mutex = null;
            Exit();
            return;
        }
        try
        {
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var dispatcher = new UiDispatcher(queue);
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowGather");
            var startup = await Task.Run(() =>
            {
                var engine = new GatherEngine(new NativeDesktop(), new SessionStore(directory));
                var store = new ShortcutStore(directory);
                ShortcutSettings settings;
                string warning = "";
                try { settings = store.Load(); }
                catch (Exception error)
                {
                    settings = ShortcutSettings.Defaults;
                    warning = $"Cannot load saved shortcuts: {error.Message}\nUsing defaults until you save repaired settings.";
                }
                return (engine, store, settings, warning);
            });
            shell = new WindowsShell(dispatcher.InvokeAsync);
            identifier = new NativeDisplayIdentifier();
            shortcuts = new ShortcutController(shell, startup.store);
            coordinator = new OperationCoordinator(startup.engine);
            model = new MainViewModel(coordinator, shortcuts, dispatcher, startup.warning);
            model.UpdateShortcuts(shortcuts.Activate(startup.settings));
            window = new MainWindow(model, () => shortcuts.Settings);
            window.ExitRequested += ExitApplication;
            model.AttentionRequested += window.ShowWindow;
            model.IdentifyRequested += () =>
            {
                try { identifier.ShowDisplays(model.Displays.ToArray()); }
                catch (Exception error) { model.ReportProblem("Cannot identify displays: " + error.Message); }
            };
            shell.HotkeyPressed += async id =>
            {
                if (id == shortcuts.GatherId) await model.ExecuteAsync(OperationKind.GatherPointer);
                else if (id == shortcuts.RestoreId) await model.ExecuteAsync(OperationKind.Restore);
            };
            shell.GatherRequested += async display => await model.ExecuteAsync(OperationKind.GatherSelected, display);
            shell.ActionRequested += async action =>
            {
                switch (action)
                {
                    case ShellAction.Open: window.ShowWindow(); break;
                    case ShellAction.Restore: await model.ExecuteAsync(OperationKind.Restore); break;
                    case ShellAction.Identify: model.IdentifyCommand.Execute(null); break;
                    case ShellAction.Shortcuts: model.ChangeShortcutsCommand.Execute(null); break;
                    case ShellAction.Forget: await window.ConfirmForgetAsync(); break;
                    case ShellAction.Exit: ExitApplication(); break;
                }
            };
            shell.Problem += model.ReportProblem;
            shell.TopologyChanged += async () => await model.NotifyTopologyChangedAsync();
            coordinator.StateChanged += state =>
            {
                if (!dispatcher.TryEnqueue(() =>
                {
                    try { shell.Update(state); }
                    catch (Exception error) { model.ReportProblem(error.Message); }
                }))
                    System.Diagnostics.Trace.TraceError("Tray update rejected during shutdown.");
            };
            window.ShowWindow();
            await model.ExecuteAsync(OperationKind.Refresh);
            if (coordinator.State.HasRecovery)
                model.Summary = $"Recovery available: {coordinator.State.BorrowedCount} borrowed windows. Click Restore to send them home.";
            else if (!model.HasDetails) model.Summary = "Ready. Choose the display you want to work on.";
            await model.NotifyTopologyChangedAsync();
        }
        catch (Exception error)
        {
            NativeMessage.Show($"Window Gather could not start.\n\n{error.Message}\n\nExisting recovery will not be overwritten.", "Window Gather");
            ExitApplication();
        }
#endif
    }

#if !UI_PARITY_TEST
    private void ExitApplication()
    {
        if (model?.IsBusy == true || coordinator?.HasWorkPending == true) return;
        try { shortcuts?.Release(); }
        catch (Exception error) { NativeMessage.Show(error.Message, "Cannot release shortcuts"); return; }
        identifier?.Dispose();
        shell?.Dispose();
        model?.Dispose();
        window?.ExitWindow();
        mutex?.Dispose();
        mutex = null;
        Exit();
    }
#endif
}
