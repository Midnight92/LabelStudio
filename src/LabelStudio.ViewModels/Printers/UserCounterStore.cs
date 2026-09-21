using LabelStudio.Core;

namespace LabelStudio.ViewModels.Printers;

/// <summary>
/// The app's resettable label counter (spec §5 Counters): stores, per printer serial, the printer's own label
/// count at the last reset. The displayed value is the current count minus this baseline.
/// </summary>
public sealed class UserCounterStore(string path)
{
    private readonly JsonFileStore<Dictionary<string, long>> _store = new(path);

    public long? GetBaseline(string serial) => _store.Load() is { } all && all.TryGetValue(serial, out var v) ? v : null;

    public void SetBaseline(string serial, long value)
    {
        var all = _store.Load() ?? new Dictionary<string, long>(StringComparer.Ordinal);
        all[serial] = value;
        _store.Save(all);
    }
}
