namespace PoolPumpOptimizer.Wpf.Models;

public sealed record PriceSlot(
    DateTimeOffset StartsAt,
    decimal PriceSekPerKwh)
{
    public DateOnly LocalDate => DateOnly.FromDateTime(StartsAt.LocalDateTime);
}