using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LabelStudio.Devices;
using LabelStudio.Devices.Models;

namespace LabelStudio.ViewModels.Reference;

public sealed record BlinkCodeRow(string Model, string Kind, string Pattern, string Meaning, string Action, string Source);

/// <summary>
/// The connected printer's model if it is known; with no printer, every model in the catalog, because
/// the reference is most useful exactly when the printer isn't talking to the app.
/// </summary>
public sealed partial class BlinkCodesViewModel : ObservableObject
{
    private readonly IReadOnlyList<BlinkCodeRow> _all;

    public BlinkCodesViewModel(DeviceService devices)
    {
        var profile = devices.Snapshot.Profile;
        var models = profile is null ? BlinkCodeCatalog.Models : [ModelCatalog.For(profile).ProductName];
        _all = models.SelectMany(m => BlinkCodeCatalog.For(m).Select(c => Row(m, c))).ToList();
        ModelHeading = profile is null ? Strings.Get("BlinkCodes.HeadingAll") : Strings.Format("BlinkCodes.Heading", profile.VariantName);
        HasNoReference = _all.Count == 0;
        SearchText = ""; // runs Filter via OnSearchTextChanged
    }

    public ObservableCollection<BlinkCodeRow> Items { get; } = [];
    public string ModelHeading { get; }
    public bool HasNoReference { get; }

    // Partial properties can't have initializers; the constructor sets SearchText.
    [ObservableProperty] public partial string SearchText { get; set; }
    [ObservableProperty] public partial bool HasNoMatches { get; set; }

    partial void OnSearchTextChanged(string value) => Filter();

    private void Filter()
    {
        var term = (SearchText ?? "").Trim();
        Items.Clear();
        foreach (var row in _all.Where(r => term.Length == 0
                     || r.Pattern.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                     || r.Meaning.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                     || r.Action.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            Items.Add(row);
        HasNoMatches = !HasNoReference && Items.Count == 0;
    }

    private static BlinkCodeRow Row(string model, BlinkCode c) => new(
        model,
        Strings.Get($"BlinkCode.Kind.{c.Kind}"),
        Strings.Get($"BlinkCode.{model}.{c.Id}.Pattern"),
        Strings.Get($"BlinkCode.{model}.{c.Id}.Meaning"),
        Strings.Get($"BlinkCode.{model}.{c.Id}.Action"),
        Strings.Format("BlinkCode.Source", c.SourcePage));
}
