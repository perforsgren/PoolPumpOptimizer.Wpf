using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PoolPumpOptimizer.Wpf.Models;

namespace PoolPumpOptimizer.Wpf.Services;

public sealed class ShellyCloudClient : IDisposable
{
    private readonly PoolPumpConfig _config;
    private readonly HttpClient _http;

    public ShellyCloudClient(PoolPumpConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _http = new HttpClient(CreateHttpClientHandler(config))
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Testar Shelly Cloud-anslutningen genom att läsa den konfigurerade
    /// enhetens status. Metoden returnerar statusen om anropet lyckas.
    /// </summary>
    public Task<ShellySwitchStatus> TestConnectionAsync()
    {
        return GetSwitchStatusAsync();
    }

    /// <summary>
    /// Läser aktuell enhets- och switchstatus från Shelly Cloud Control API v2.
    /// </summary>
    public async Task<ShellySwitchStatus> GetSwitchStatusAsync()
    {
        ValidateConfiguration();

        var server = _config.ShellyCloudServer.Trim().TrimEnd('/');
        var authKey = Uri.EscapeDataString(_config.ShellyCloudAuthKey.Trim());
        var endpoint = $"{server}/v2/devices/api/get?auth_key={authKey}";

        var body = new
        {
            ids = new[] { _config.ShellyDeviceId.Trim() },
            select = new[] { "status" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shelly Cloud returnerade HTTP {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}). Svar: {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Shelly Cloud returnerade ett oväntat svar. Rotobjektet var inte en lista.");
        }

        if (document.RootElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Shelly Cloud returnerade ingen enhet för angivet Device ID.");
        }

        var wantedDeviceId = _config.ShellyDeviceId.Trim();
        JsonElement? selectedDevice = null;

        foreach (var device in document.RootElement.EnumerateArray())
        {
            var deviceId = GetStringOrNull(device, "id");

            if (string.Equals(
                    deviceId,
                    wantedDeviceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                selectedDevice = device;
                break;
            }
        }

        selectedDevice ??= document.RootElement[0];

        return ParseStatus(selectedDevice.Value);
    }

    /// <summary>
    /// Sätter switchens utgång explicit till PÅ eller AV via Shelly Cloud.
    /// Efter kommandot väntar metoden minst en sekund och läser tillbaka status
    /// för att verifiera att det begärda läget faktiskt har uppnåtts.
    /// </summary>
    public async Task<ShellySwitchStatus> SetSwitchStateAsync(
        bool turnOn,
        ShellySwitchStatus? knownStatus = null)
    {
        ValidateConfiguration();

        var currentStatus = knownStatus ?? await GetSwitchStatusAsync();

        if (!currentStatus.IsOnline)
        {
            throw new InvalidOperationException(
                "Shelly-enheten är offline och kan inte styras.");
        }

        if (currentStatus.IsOn == turnOn)
            return currentStatus;

        // Shelly Cloud Control API är begränsat till ett anrop per sekund.
        // Väntan behövs både när statusen lästes här och när anroparen
        // skickade in en nyligen läst status.
        await Task.Delay(TimeSpan.FromMilliseconds(1100));

        var server = _config.ShellyCloudServer.Trim().TrimEnd('/');
        var authKey = Uri.EscapeDataString(_config.ShellyCloudAuthKey.Trim());
        var endpoint = $"{server}/v2/devices/api/set/switch?auth_key={authKey}";

        var body = new
        {
            id = _config.ShellyDeviceId.Trim(),
            channel = _config.ShellySwitchId,
            on = turnOn
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Shelly Cloud kunde inte sätta switchen till " +
                $"{(turnOn ? "PÅ" : "AV")}. HTTP {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}). Svar: {responseText}");
        }

        // Vänta innan verifieringsläsningen för att respektera rate limit
        // och ge enheten tid att rapportera det nya tillståndet.
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        var verifiedStatus = await GetSwitchStatusAsync();

        if (!verifiedStatus.IsOnline)
        {
            throw new InvalidOperationException(
                "Kommandot skickades men enheten var offline vid verifieringen.");
        }

        if (verifiedStatus.IsOn != turnOn)
        {
            throw new InvalidOperationException(
                $"Kommandot skickades men verifieringen misslyckades. " +
                $"Begärt läge: {(turnOn ? "PÅ" : "AV")}. " +
                $"Rapporterat läge: {verifiedStatus.OutputText}.");
        }

        return verifiedStatus;
    }

    /// <summary>
    /// Frigör den underliggande HttpClient-instansen.
    /// </summary>
    public void Dispose()
    {
        _http.Dispose();
    }

    private ShellySwitchStatus ParseStatus(JsonElement device)
    {
        var deviceId = GetStringOrNull(device, "id")
                       ?? _config.ShellyDeviceId.Trim();

        var deviceType = GetStringOrNull(device, "type");
        var deviceCode = GetStringOrNull(device, "code");
        var generation = GetStringOrNull(device, "gen");
        var isOnline = GetOnline(device);
        var switchId = _config.ShellySwitchId;

        bool? isOn = null;
        decimal? activePower = null;
        decimal? voltage = null;
        decimal? current = null;
        decimal? powerFactor = null;
        decimal? frequency = null;
        decimal? totalEnergy = null;
        long? onTimeMinutes = null;
        long? switchOnCount = null;

        if (device.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.Object)
        {
            var switchPropertyName = $"switch:{switchId}";

            if (status.TryGetProperty(switchPropertyName, out var switchStatus) &&
                switchStatus.ValueKind == JsonValueKind.Object)
            {
                isOn = GetBooleanOrNull(switchStatus, "output");
                activePower = GetDecimalOrNull(switchStatus, "apower");
                voltage = GetDecimalOrNull(switchStatus, "voltage");
                current = GetDecimalOrNull(switchStatus, "current");
                powerFactor = GetDecimalOrNull(switchStatus, "pf");
                frequency = GetDecimalOrNull(switchStatus, "freq");

                if (switchStatus.TryGetProperty("aenergy", out var activeEnergy) &&
                    activeEnergy.ValueKind == JsonValueKind.Object)
                {
                    totalEnergy = GetDecimalOrNull(activeEnergy, "total");
                }

                if (switchStatus.TryGetProperty("counts", out var counts) &&
                    counts.ValueKind == JsonValueKind.Object)
                {
                    onTimeMinutes = GetLongOrNull(counts, "on_time");
                    switchOnCount = GetLongOrNull(counts, "switch_on");
                }
            }
            else if (isOnline)
            {
                throw new InvalidOperationException(
                    $"Shelly-enheten är online men statusen saknar komponenten " +
                    $"'{switchPropertyName}'. Kontrollera Switch-ID.");
            }
        }
        else if (isOnline)
        {
            throw new InvalidOperationException(
                "Shelly-enheten är online men svaret saknar statusdata.");
        }

        return new ShellySwitchStatus(
            DeviceId: deviceId,
            DeviceType: deviceType,
            DeviceCode: deviceCode,
            Generation: generation,
            IsOnline: isOnline,
            SwitchId: switchId,
            IsOn: isOn,
            ActivePowerWatt: activePower,
            VoltageVolt: voltage,
            CurrentAmpere: current,
            PowerFactor: powerFactor,
            FrequencyHertz: frequency,
            TotalEnergyWattHours: totalEnergy,
            OnTimeMinutes: onTimeMinutes,
            SwitchOnCount: switchOnCount,
            ReadAtLocal: DateTime.Now);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_config.ShellyCloudServer))
        {
            throw new InvalidOperationException(
                "Shelly Cloud-server saknas. Ange den i Inställningar.");
        }

        if (!Uri.TryCreate(
                _config.ShellyCloudServer.Trim(),
                UriKind.Absolute,
                out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttps &&
             serverUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Shelly Cloud-servern är ogiltig. Ange hela adressen, exempelvis " +
                "https://shelly-268-eu.shelly.cloud.");
        }

        if (string.IsNullOrWhiteSpace(_config.ShellyCloudAuthKey))
        {
            throw new InvalidOperationException(
                "Shelly Cloud-nyckel saknas. Ange nyckeln i Inställningar.");
        }

        if (string.IsNullOrWhiteSpace(_config.ShellyDeviceId))
        {
            throw new InvalidOperationException(
                "Shelly Device ID saknas. Ange Device ID i Inställningar.");
        }

        if (_config.ShellySwitchId < 0)
        {
            throw new InvalidOperationException(
                "Shelly Switch-ID får inte vara negativt.");
        }
    }

    private static HttpClientHandler CreateHttpClientHandler(PoolPumpConfig config)
    {
        if (!config.UseProxy)
        {
            return new HttpClientHandler
            {
                UseProxy = false
            };
        }

        if (string.IsNullOrWhiteSpace(config.ProxyAddress))
        {
            return new HttpClientHandler
            {
                UseProxy = true,
                UseDefaultCredentials = true,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials
            };
        }

        return new HttpClientHandler
        {
            UseProxy = true,
            Proxy = new WebProxy(config.ProxyAddress)
            {
                UseDefaultCredentials = true
            },
            UseDefaultCredentials = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
    }

    private static bool GetOnline(JsonElement device)
    {
        if (!device.TryGetProperty("online", out var online))
            return false;

        return online.ValueKind switch
        {
            JsonValueKind.Number => online.TryGetInt32(out var value) && value == 1,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false
        };
    }

    private static string? GetStringOrNull(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool? GetBooleanOrNull(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static decimal? GetDecimalOrNull(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.TryGetDecimal(out var value)
            ? value
            : null;
    }

    private static long? GetLongOrNull(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (property.TryGetInt64(out var integerValue))
            return integerValue;

        if (property.TryGetDecimal(out var decimalValue))
            return (long)Math.Round(decimalValue, MidpointRounding.AwayFromZero);

        return null;
    }
}
