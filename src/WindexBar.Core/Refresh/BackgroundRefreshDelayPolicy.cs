namespace WindexBar.Core.Refresh;

public sealed class BackgroundRefreshDelayPolicy
{
    public const int BaseDelaySeconds = 30;
    public const int MaximumDelaySeconds = 120;
    private int _consecutiveFailures;

    public TimeSpan NextDelay => TimeSpan.FromSeconds(
        Volatile.Read(ref _consecutiveFailures) switch
        {
            <= 0 => BaseDelaySeconds,
            1 => BaseDelaySeconds * 2,
            _ => MaximumDelaySeconds
        });

    public void RecordResult(bool succeeded)
    {
        if (succeeded)
        {
            Reset();
            return;
        }

        while (true)
        {
            var current = Volatile.Read(ref _consecutiveFailures);
            if (current >= 2
                || Interlocked.CompareExchange(ref _consecutiveFailures, current + 1, current) == current)
            {
                return;
            }
        }
    }

    public void Reset() => Volatile.Write(ref _consecutiveFailures, 0);
}
