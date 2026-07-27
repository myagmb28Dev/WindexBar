namespace WindexBar.Core.Models;

public sealed record RateWindow(
    double UsedPercent,
    int? WindowMinutes,
    DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Max(0, 100 - UsedPercent);
}
