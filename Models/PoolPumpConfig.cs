namespace PoolPumpOptimizer.Wpf.Models;

public sealed class PoolPumpConfig
{
    public string TibberToken { get; set; } = "";

    public string? PreferredTibberHomeId { get; set; }
    public string? PreferredTibberHomeNickname { get; set; }

    public int MinRunHoursPerDay { get; set; } = 8;
    public int PreferredBlockHours { get; set; } = 2;
    public int MaxStartsPerDay { get; set; } = 4;

    public decimal CheapPriceThresholdSekPerKwh { get; set; } = 0.50m;

    public int MinOnMinutes { get; set; } = 90;
    public int MinOffMinutes { get; set; } = 60;

    public bool IgnorePastSlots { get; set; } = true;

    public bool UseProxy { get; set; } = true;
    public string? ProxyAddress { get; set; } = "http://proxyvip.foreningssparbanken.se:8080";

    public bool PrintRawTibberJson { get; set; } = false;
}