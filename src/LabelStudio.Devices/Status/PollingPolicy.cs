namespace LabelStudio.Devices.Status;

/// <summary>Spec §4: 3 s idle, 500 ms during a job, 30 s after three consecutive failures.</summary>
public static class PollingPolicy
{
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Active = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Backoff = TimeSpan.FromSeconds(30);
    public const int FailuresBeforeBackoff = 3;

    public static TimeSpan NextDelay(bool jobActive, int consecutiveFailures) =>
        consecutiveFailures >= FailuresBeforeBackoff ? Backoff : jobActive ? Active : Idle;
}
