namespace LabelStudio.Core;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Update(Func<AppSettings, AppSettings> change);
}

public sealed class SettingsService : ISettingsService
{
    private readonly JsonFileStore<AppSettings> _store;
    private readonly Lock _lock = new();

    public SettingsService(JsonFileStore<AppSettings> store)
    {
        _store = store;
        Current = store.Load() ?? new AppSettings();
    }

    public AppSettings Current { get; private set; }

    public void Update(Func<AppSettings, AppSettings> change)
    {
        lock (_lock)
        {
            var next = change(Current);
            if (next == Current) return;
            Current = next;
            _store.Save(next);
        }
    }
}
