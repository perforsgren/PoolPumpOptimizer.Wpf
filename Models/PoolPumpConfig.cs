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

    /// <summary>
    /// Shelly Cloud-servern som visas i Shelly-appen under molninställningar.
    /// Exempel: https://shelly-268-eu.shelly.cloud
    /// </summary>
    public string ShellyCloudServer { get; set; } = "";

    /// <summary>
    /// Nyckel för molnåtkomst från Shelly-appen.
    /// Sparas öppet i settings.json enligt vald v1-lösning.
    /// </summary>
    public string ShellyCloudAuthKey { get; set; } = "";

    /// <summary>
    /// Shelly-enhetens Device ID, exempelvis D885AC173570.
    /// </summary>
    public string ShellyDeviceId { get; set; } = "";

    /// <summary>
    /// ID för switch-komponenten. För Shelly Plug S Gen3 är detta normalt 0.
    /// </summary>
    public int ShellySwitchId { get; set; }

    /// <summary>
    /// Aktiverar manuella PÅ/AV-kommandon från applikationen.
    /// Automatisk planstyrning ingår inte i denna fas.
    /// </summary>
    public bool ShellyManualControlEnabled { get; set; }

    /// <summary>
    /// Tillåter automatisk styrning enligt den aktuella optimeringsplanen.
    /// Styrningen måste ändå startas manuellt efter varje programstart.
    /// </summary>
    public bool ShellyAutomaticControlEnabled { get; set; }

    /// <summary>
    /// Intervall mellan automatiska kontrollcykler mot Shelly Cloud.
    /// </summary>
    public int ShellyControlPollSeconds { get; set; } = 30;
}
