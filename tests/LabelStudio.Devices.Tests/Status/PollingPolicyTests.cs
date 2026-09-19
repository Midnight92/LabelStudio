using LabelStudio.Devices.Status;

namespace LabelStudio.Devices.Tests.Status;

public class PollingPolicyTests
{
    [Theory]
    [InlineData(false, 0, 3000)]
    [InlineData(true, 0, 500)]
    [InlineData(true, 2, 500)]
    [InlineData(false, 3, 30000)]
    [InlineData(true, 5, 30000)]
    public void Delay_matches_spec(bool jobActive, int failures, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), PollingPolicy.NextDelay(jobActive, failures));
}
