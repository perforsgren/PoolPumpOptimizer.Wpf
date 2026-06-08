namespace PoolPumpOptimizer.Wpf.Models;

public sealed record RunBlock(
    DateTimeOffset Start,
    DateTimeOffset End,
    TimeSpan Duration,
    decimal AveragePrice)
{
    public string DateText => Start.ToString("yyyy-MM-dd");

    public string StartText => Start.ToString("HH:mm");

    public string EndText => End.ToString("HH:mm");

    public string DurationText => $"{Duration.TotalHours:0.00} h";

    public string AveragePriceText => $"{AveragePrice:0.000} SEK/kWh";
}