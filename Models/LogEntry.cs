namespace PoolPumpOptimizer.Wpf.Models;

public sealed record LogEntry(
    DateTime Timestamp,
    string Level,
    string Message)
{
    public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
}