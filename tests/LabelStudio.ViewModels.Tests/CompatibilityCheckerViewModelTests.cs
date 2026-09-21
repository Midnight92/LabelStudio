using LabelStudio.Devices.Simulation;
using LabelStudio.Tests;
using LabelStudio.ViewModels.Printers;

namespace LabelStudio.ViewModels.Tests;

public class CompatibilityCheckerViewModelTests
{
    private static async Task<(CompatibilityCheckerViewModel Vm, IAsyncDisposable Svc)> CreateAsync(TempDir dir)
    {
        var (svc, _, _) = TestDevices.Create(dir, new SimulatedPrinter());
        await svc.StartAsync(CancellationToken.None);
        return (new CompatibilityCheckerViewModel(svc, new ImmediateDispatcher()), svc);
    }

    [Fact]
    public async Task Questions_follow_the_chosen_media_kind()
    {
        using var dir = new TempDir();
        var (vm, svc) = await CreateAsync(dir);
        await using var _ = svc;
        Assert.True(vm.IsAvailable);
        Assert.False(vm.ShowGapQuestion);
        vm.SensingIndex = 0; // gap/notch
        Assert.True(vm.ShowGapQuestion);
        Assert.False(vm.ShowMarkQuestion);
        vm.SensingIndex = 1; // black mark
        Assert.True(vm.ShowMarkQuestion);
        Assert.False(vm.ShowGapQuestion);
        Assert.Contains(1.0.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture), vm.SizeQuestion);
    }

    [Fact]
    public async Task Incompatible_answers_give_a_verdict_with_reasons_and_offer_length_only()
    {
        using var dir = new TempDir();
        var (vm, svc) = await CreateAsync(dir);
        await using var _ = svc;
        var requested = false;
        vm.LengthOnlyRequested += (_, _) => requested = true;

        vm.SensingIndex = 0;
        vm.GapAnswerIndex = 1; // no
        Assert.False(vm.IsVerdictVisible);
        vm.SizeAnswerIndex = 0; // yes

        Assert.True(vm.IsVerdictVisible);
        Assert.False(vm.IsCompatible);
        Assert.Single(vm.Reasons);
        vm.UseLengthOnlyCommand.Execute(null);
        Assert.True(requested);
    }

    [Fact]
    public async Task Continuous_media_is_compatible_but_is_not_told_to_calibrate()
    {
        using var dir = new TempDir();
        var (vm, svc) = await CreateAsync(dir);
        await using var _ = svc;

        vm.SensingIndex = 2; // continuous
        vm.SizeAnswerIndex = 0; // yes

        Assert.True(vm.IsVerdictVisible);
        Assert.True(vm.IsCompatible);
        // Continuous media has no gaps to measure, and calibrating against media the sensor can't read feeds
        // roughly 18 labels before failing, so the verdict must not tell the operator to run SmartCal.
        Assert.DoesNotContain("Run SmartCal", vm.VerdictBody);
        Assert.Contains("length", vm.VerdictBody);
        Assert.True(vm.IsLengthOnlyOffered);
    }

    [Fact]
    public async Task Gap_media_that_suits_the_sensors_is_still_sent_to_calibration()
    {
        using var dir = new TempDir();
        var (vm, svc) = await CreateAsync(dir);
        await using var _ = svc;

        vm.SensingIndex = 0; // gap/notch
        vm.GapAnswerIndex = 0; // centred
        vm.SizeAnswerIndex = 0; // yes

        Assert.True(vm.IsCompatible);
        Assert.Contains("Run SmartCal", vm.VerdictBody);
        Assert.False(vm.IsLengthOnlyOffered);
    }

    [Fact]
    public async Task Reset_clears_every_answer()
    {
        using var dir = new TempDir();
        var (vm, svc) = await CreateAsync(dir);
        await using var _ = svc;
        vm.SensingIndex = 2;
        vm.SizeAnswerIndex = 0;
        Assert.True(vm.IsCompatible);
        vm.ResetCommand.Execute(null);
        Assert.Equal(-1, vm.SensingIndex);
        Assert.False(vm.IsVerdictVisible);
    }
}
