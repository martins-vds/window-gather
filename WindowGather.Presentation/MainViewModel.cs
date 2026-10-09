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
    private string loadWarning;
    private long appliedRevision = -1;
    private long presentedCompletion = -1;

    public MainViewModel(OperationCoordinator coordinator, ShortcutController shortcuts,
        IUiDispatcher dispatcher, string loadWarning = "")
    {
        this.coordinator = coordinator;
        this.shortcuts = shortcuts;
        this.dispatcher = dispatcher;
        this.loadWarning = loadWarning;
        coordinator.StateChanged += OnStateChanged;
        UpdateShortcuts([]);
        ApplyState(coordinator.State);
        Summary = HasRecovery ? $"Recovery available: {BorrowedCount} borrowed windows. Click Restore to send them home."
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
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GatherCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForgetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    [NotifyPropertyChangedFor(nameof(CanRestoreRecovery))]
    public partial bool HasRecovery { get; set; }

    [ObservableProperty] public partial int BorrowedCount { get; set; }
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
            $"{shortcuts.Settings.Restore}  Restore borrowed windows\n" +
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
