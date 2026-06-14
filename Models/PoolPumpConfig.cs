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

    /// <summary>
    /// Minsta sammanhängande period med extra billigt pris som får
    /// skapa ett nytt fristående körblock.
    ///
    /// En kortare billigperiod kan fortfarande förlänga ett redan
    /// planerat block utan att skapa en ny start.
    /// </summary>
    public int MinExtraCheapBlockMinutes { get; set; } = 60;

    public int MinOnMinutes { get; set; } = 90;

    public int MinOffMinutes { get; set; } = 60;

    public bool IgnorePastSlots { get; set; } = true;

    public bool UseProxy { get; set; } = true;

    public string? ProxyAddress { get; set; } =
        "http://proxyvip.foreningssparbanken.se:8080";

    public bool PrintRawTibberJson { get; set; }
}