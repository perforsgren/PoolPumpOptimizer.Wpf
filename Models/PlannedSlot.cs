namespace PoolPumpOptimizer.Wpf.Models;

public sealed record PlannedSlot(
    DateTimeOffset StartsAt,
    decimal PriceSekPerKwh,
    bool Run,
    string Reason)
{
    public DateOnly LocalDate => DateOnly.FromDateTime(StartsAt.LocalDateTime);

    public string StartsAtText => StartsAt.ToString("yyyy-MM-dd HH:mm");

    public string PriceText => PriceSekPerKwh.ToString("0.000");

    public string RunText => Run ? "ON" : "OFF";
}