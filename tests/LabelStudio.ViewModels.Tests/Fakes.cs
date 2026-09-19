using LabelStudio.ViewModels;

namespace LabelStudio.ViewModels.Tests;

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class RecordingNavigation : INavigationService
{
    public List<string> Visited { get; } = [];
    public void NavigateTo(string pageKey) => Visited.Add(pageKey);
}
