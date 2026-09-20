using LabelStudio.ViewModels;
using Microsoft.Extensions.Logging;

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

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, exception));
}
