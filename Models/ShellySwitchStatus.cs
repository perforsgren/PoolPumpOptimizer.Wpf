namespace PoolPumpOptimizer.Wpf.Models;

public sealed record ShellySwitchStatus(
    string DeviceId,
    string? DeviceType,
    string? DeviceCode,
    string? Generation,
    bool IsOnline,
    int SwitchId,
    bool? IsOn,
    decimal? ActivePowerWatt,
    decimal? VoltageVolt,
    decimal? CurrentAmpere,
    decimal? PowerFactor,
    decimal? FrequencyHertz,
    decimal? TotalEnergyWattHours,
    long? OnTimeMinutes,
    long? SwitchOnCount,
    DateTime ReadAtLocal)
{
    public string OnlineText => IsOnline ? "Online" : "Offline";

    public string OutputText => IsOn switch
    {
        true => "På",
        false => "Av",
        null => "Okänd"
    };

    public string ActivePowerText =>
        ActivePowerWatt.HasValue
            ? $"{ActivePowerWatt.Value:0.0} W"
            : "-";

    public string VoltageText =>
        VoltageVolt.HasValue
            ? $"{VoltageVolt.Value:0.0} V"
            : "-";

    public string CurrentText =>
        CurrentAmpere.HasValue
            ? $"{CurrentAmpere.Value:0.000} A"
            : "-";

    public string OnTimeText
    {
        get
        {
            if (!OnTimeMinutes.HasValue)
                return "-";

            var duration = TimeSpan.FromMinutes(OnTimeMinutes.Value);

            return duration.TotalHours >= 24
                ? $"{(int)duration.TotalHours}:{duration.Minutes:00}"
                : $"{duration.Hours:00}:{duration.Minutes:00}";
        }
    }

    public string SwitchOnCountText =>
        SwitchOnCount?.ToString() ?? "-";

    public string ReadAtTimeText =>
        ReadAtLocal.ToString("HH:mm:ss");

    public string ReadAtTooltipText =>
        ReadAtLocal.ToString("yyyy-MM-dd HH:mm:ss");
}
