namespace PoolPumpOptimizer.Wpf.Models;

/// <summary>
/// Skrivskyddad ögonblicksbild av lokalt beräknad pumpstatistik.
/// </summary>
public sealed record PumpRuntimeSnapshot(
    DateOnly TrackingDate,
    bool? CurrentState,
    TimeSpan RunTimeToday,
    int StartsToday,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastStoppedAt,
    DateTimeOffset? LastObservedAt)
{
    public string RunTimeTodayText =>
        $"{(int)RunTimeToday.TotalHours:00}:{RunTimeToday.Minutes:00}:{RunTimeToday.Seconds:00}";

    public string StartsTodayText =>
        StartsToday.ToString();

    public string LastStartedText =>
        LastStartedAt.HasValue
            ? LastStartedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "Ingen observerad start ännu";

    public string LastStoppedText =>
        LastStoppedAt.HasValue
            ? LastStoppedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "Inget observerat stopp ännu";

    public string LastObservedText =>
        LastObservedAt.HasValue
            ? LastObservedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "Ingen status observerad ännu";
}
