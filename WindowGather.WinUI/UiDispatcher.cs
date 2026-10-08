using Microsoft.UI.Dispatching;
using WindowGather.Presentation;

namespace WindowGather.WinUI;

internal sealed class UiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool TryEnqueue(Action action)
    {
        if (queue.HasThreadAccess) { action(); return true; }
        return queue.TryEnqueue(() => action());
    }

    public Task InvokeAsync(Action action)
    {
        if (queue.HasThreadAccess) { action(); return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }))
            completion.SetException(new InvalidOperationException("The UI dispatcher is shutting down."));
        return completion.Task;
    }
}
