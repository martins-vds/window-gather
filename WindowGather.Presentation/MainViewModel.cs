using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WindowGather.Presentation;

public interface IUiDispatcher
{
    bool TryEnqueue(Action action);
}

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly OperationCoordinator coordinator;
    private readonly IUiDispatcher dispatcher;
    private readonly ShortcutController shortcuts;
    private readonly StartupController? startup;
    private string loadWarning;
    private long appliedRevision = -1;
    private long presentedCompletion = -1;

    public MainViewModel(OperationCoordinator coordinator, ShortcutController shortcuts,
        IUiDispatcher dispatcher, string loadWarning = "", StartupController? startup = null)
    {
        this.coordinator = coordinator;
        this.shortcuts = shortcuts;
        this.dispatcher = dispatcher;
        this.loadWarning = loadWarning;
        this.startup = startup;
        ApplyStartupState();
        coordinator.StateChanged += OnStateChanged;
        UpdateShortcuts([]);
        ApplyState(coordinator.State);
        Summary = HasRecovery ? "Recovery available. Click Restore session to finish saved window and display recovery."
            : "Ready. Choose the display you want to work on.";
    }

    public ObservableCollection<Display> Displays { get; } = [];
    public event Action? IdentifyRequested;
    public event Action? ShortcutsRequested;
    public event Action? AttentionRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GatherCommand))]
    public partial Display? SelectedDisplay { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GatherCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(IdentifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeShortcutsCommand))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    [NotifyPropertyChangedFor(nameof(CanRestoreRecovery))]
    [NotifyPropertyChangedFor(nameof(CanFollowOptions))]
    [NotifyPropertyChangedFor(nameof(CanChangeStartup))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GatherCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    [NotifyPropertyChangedFor(nameof(CanRestoreRecovery))]
    [NotifyPropertyChangedFor(nameof(CanFollowOptions))]
    public partial bool HasRecovery { get; set; }

    [ObservableProperty] public partial int BorrowedCount { get; set; }
    [ObservableProperty] public partial int FollowerCount { get; set; }
    [ObservableProperty] public partial bool HasPrimaryRecovery { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFollowOptions))]
    public partial bool MakeDestinationPrimary { get; set; }
    [ObservableProperty] public partial bool FollowNewWindows { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartupWarning))]
    public partial string StartupWarning { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptionsWarning))]
    public partial string OptionsWarning { get; set; } = "";
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    public partial string Details { get; set; } = "";
    [ObservableProperty] public partial string ShortcutReference { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutWarning))]
    public partial string ShortcutWarning { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoveryWarning))]
    public partial string RecoveryWarning { get; set; } = "";

    public bool CanSelect => !IsBusy && !HasRecovery;
    public bool CanChangeStartup => !IsBusy && startup is not null;
    public bool HasStartupWarning => StartupWarning.Length > 0;
    public bool CanFollowOptions => CanSelect && MakeDestinationPrimary;
    public bool HasOptionsWarning => OptionsWarning.Length > 0;
    public bool CanRestoreRecovery => !IsBusy && HasRecovery;
    public bool HasDetails => Details.Length > 0;
    public bool HasShortcutWarning => ShortcutWarning.Length > 0;
    public bool HasRecoveryWarning => RecoveryWarning.Length > 0;
    private bool CanGather() => CanSelect && SelectedDisplay is not null;
    private bool CanRestore() => !IsBusy && HasRecovery;
    private bool CanRefresh() => CanSelect;
    private bool CanIdentify() => !IsBusy && Displays.Count > 0;
    private bool CanChangeShortcuts() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanGather))]
    private Task GatherAsync() => ExecuteAsync(OperationKind.GatherSelected, SelectedDisplay);
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task RestoreAsync() => ExecuteAsync(OperationKind.Restore);
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task ForgetAsync() => ExecuteAsync(OperationKind.Forget);
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => ExecuteAsync(OperationKind.Refresh);
    [RelayCommand(CanExecute = nameof(CanIdentify))]
    private void Identify() => IdentifyRequested?.Invoke();
    [RelayCommand(CanExecute = nameof(CanChangeShortcuts))]
    private void ChangeShortcuts() => ShortcutsRequested?.Invoke();

    public async Task ExecuteAsync(OperationKind kind, Display? target = null)
    {
        OperationReply reply = await coordinator.ExecuteAsync(kind, target).ConfigureAwait(false);
        Dispatch(() => Present(reply));
    }

    public async Task NotifyTopologyChangedAsync()
    {
        OperationReply? reply = await coordinator.NotifyTopologyChangedAsync().ConfigureAwait(false);
        if (reply is not null) Dispatch(() => Present(reply, requestAttention: false));
    }

    public async Task SaveGatherOptionsAsync(GatherPreferences preferences)
    {
        OperationReply reply = await coordinator.SavePreferencesAsync(preferences).ConfigureAwait(false);
        Dispatch(() => Present(reply));
    }

    public async Task SaveStartupAsync(bool enabled)
    {
        OperationReply reply = await coordinator.RunAsync(() =>
        {
            if (startup is null) throw new InvalidOperationException("Windows startup is unavailable in this build.");
            startup.SetEnabled(enabled);
            return new(0, 0, [], enabled
                ? "Start with Windows enabled. Window Gather will start in the notification area after sign-in."
                : "Start with Windows disabled.");
        }).ConfigureAwait(false);
        Dispatch(() => { ApplyStartupState(); Present(reply); });
    }

    private void ApplyStartupState()
    {
        StartWithWindows = startup?.State.Enabled ?? false;
        StartupWarning = startup?.State.Warning ?? "";
    }

    public async Task TrackWindowAsync(SavedWindow window)
    {
        OperationReply reply = await coordinator.NotifyWindowOpenedAsync(window).ConfigureAwait(false);
        Dispatch(() => Present(reply, requestAttention: false));
    }

    public async Task TrackingProblemAsync(string problem)
    {
        OperationReply reply = await coordinator.NotifyTrackingProblemAsync(problem).ConfigureAwait(false);
        Dispatch(() => Present(reply, requestAttention: false));
    }

    public async Task<OperationReply> SaveShortcutsAsync(ShortcutSettings settings)
    {
        OperationReply reply = await coordinator.RunAsync(() =>
        {
            shortcuts.Apply(settings);
            return new(0, 0, [], "Shortcuts saved.");
        }).ConfigureAwait(false);
        Dispatch(() =>
        {
            if (reply.Accepted && reply.Problems.Count == 0)
            {
                loadWarning = "";
                UpdateShortcuts([]);
            }
        });
        return reply;
    }

    public void UpdateShortcuts(IReadOnlyList<string> registrationWarnings)
    {
        ShortcutReference = $"{shortcuts.Settings.Gather}  Gather onto the display under your pointer\n" +
            $"{shortcuts.Settings.Restore}  Restore session\n" +
            "Closing this window keeps the app running in the notification area.";
        ShortcutWarning = string.Join("\n", new[] { loadWarning }.Where(s => s.Length > 0).Concat(registrationWarnings));
    }

    public void ReportProblem(string message)
    {
        Details = message;
        AttentionRequested?.Invoke();
    }

    private void Present(OperationReply reply, bool requestAttention = true)
    {
        if (!reply.Announce || reply.CompletionOrder <= presentedCompletion) return;
        presentedCompletion = reply.CompletionOrder;
        Summary = reply.Summary;
        Details = string.Join(Environment.NewLine, reply.Problems);
        if (requestAttention && reply.Problems.Count > 0) AttentionRequested?.Invoke();
    }

    private void OnStateChanged(ApplicationState state) => Dispatch(() => ApplyState(state));

    private void ApplyState(ApplicationState state)
    {
        if (state.Revision <= appliedRevision) return;
        appliedRevision = state.Revision;
        string? selected = SelectedDisplay?.Id;
        if (!Displays.SequenceEqual(state.Displays))
        {
            Displays.Clear();
            foreach (Display display in state.Displays) Displays.Add(display);
        }
        HasRecovery = state.HasRecovery;
        BorrowedCount = state.BorrowedCount;
        FollowerCount = state.FollowerCount;
        HasPrimaryRecovery = state.HasPrimaryRecovery;
        MakeDestinationPrimary = state.Preferences.MakeDestinationPrimary;
        FollowNewWindows = state.Preferences.FollowNewWindows;
        OptionsWarning = state.PreferencesWarning;
        RecoveryWarning = state.RecoveryWarning;
        SelectedDisplay = state.RecoveryTarget is { } target
            ? Displays.FirstOrDefault(d => d.Id == target.Id) ?? target
            : Displays.FirstOrDefault(d => d.Id == selected) ?? Displays.FirstOrDefault();
        IsBusy = state.IsBusy;
        IdentifyCommand.NotifyCanExecuteChanged();
        if (IsBusy && !state.IsTopologyCheck) Summary = "Working... Please wait before moving windows.";
    }

    private void Dispatch(Action action)
    {
        if (!dispatcher.TryEnqueue(action))
            System.Diagnostics.Trace.TraceError("Window Gather UI dispatcher rejected a notification during shutdown.");
    }

    public void Dispose() => coordinator.StateChanged -= OnStateChanged;
}
