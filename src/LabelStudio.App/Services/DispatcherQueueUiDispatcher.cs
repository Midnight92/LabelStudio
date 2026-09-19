using LabelStudio.ViewModels;
using Microsoft.UI.Dispatching;

namespace LabelStudio.App.Services;

public sealed class DispatcherQueueUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (queue.HasThreadAccess) action();
        else queue.TryEnqueue(() => action());
    }
}
