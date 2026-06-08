using PoolPumpOptimizer.Wpf.Models;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PoolPumpOptimizer.Wpf.Services;

public sealed class TibberClient
{
    private readonly PoolPumpConfig _config;
    private readonly HttpClient _http;

    public TibberClient(PoolPumpConfig config)
    {
        _config = config;

        var handler = CreateHttpClientHandler(config);

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<List<TibberHome>> GetHomesAsync()
    {
        const string query = """
        {
          viewer {
            homes {
              id
              appNickname
              address {
                address1
              }
              currentSubscription {
                id
              }
            }
          }
        }
        """;

        using var doc = await SendGraphQlAsync(query);

        var homes = doc.RootElement
            .GetProperty("data")
            .GetProperty("viewer")
            .GetProperty("homes");

        var result = new List<TibberHome>();

        foreach (var home in homes.EnumerateArray())
        {
            var id = GetStringOrNull(home, "id");
            var nickname = GetStringOrNull(home, "appNickname");
            var address = GetAddressOrNull(home);

            var hasSubscription =
                home.TryGetProperty("currentSubscription", out var subscription) &&
                subscription.ValueKind != JsonValueKind.Null;

            result.Add(new TibberHome(
                Id: id,
                AppNickname: nickname,
                Address: address,
                HasCurrentSubscription: hasSubscription));
        }

        return result;
    }

    public async Task<List<PriceSlot>> GetQuarterPricesAsync()
    {
        const string query = """
        {
          viewer {
            homes {
              id
              appNickname
              address {
                address1
              }
              currentSubscription {
                priceInfo(resolution: QUARTER_HOURLY) {
                  today {
                    total
                    startsAt
                  }
                  tomorrow {
                    total
                    startsAt
                  }
                }
              }
            }
          }
        }
        """;

        using var doc = await SendGraphQlAsync(query);

        var homes = doc.RootElement
            .GetProperty("data")
            .GetProperty("viewer")
            .GetProperty("homes");

        JsonElement? selectedSubscription = null;

        foreach (var home in homes.EnumerateArray())
        {
            var homeId = GetStringOrNull(home, "id");
            var nickname = GetStringOrNull(home, "appNickname");

            if (!home.TryGetProperty("currentSubscription", out var subscription))
                continue;

            if (subscription.ValueKind == JsonValueKind.Null)
                continue;

            if (!IsWantedHome(homeId, nickname))
                continue;

            selectedSubscription = subscription;
            break;
        }

        if (selectedSubscription == null)
        {
            throw new InvalidOperationException(
                "Inget Tibber-home matchade valt home och hade aktiv currentSubscription.");
        }

        var priceInfo = selectedSubscription.Value.GetProperty("priceInfo");

        var result = new List<PriceSlot>();

        if (priceInfo.TryGetProperty("today", out var today) &&
            today.ValueKind == JsonValueKind.Array)
        {
            ReadPrices(today, result);
        }

        if (priceInfo.TryGetProperty("tomorrow", out var tomorrow) &&
            tomorrow.ValueKind == JsonValueKind.Array)
        {
            ReadPrices(tomorrow, result);
        }

        if (result.Count == 0)
            throw new InvalidOperationException("Inga prisrader hittades från Tibber.");

        return result
            .OrderBy(x => x.StartsAt)
            .ToList();
    }

    private async Task<JsonDocument> SendGraphQlAsync(string query)
    {
        var token = _config.TibberToken.Trim();

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Tibber token saknas.");

        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tibber token ska anges utan 'Bearer '.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.tibber.com/v1-beta/gql");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        request.Content = new StringContent(
            JsonSerializer.Serialize(new { query }),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request);

        var json = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            throw new InvalidOperationException(
                "Tibber API returnerade errors: " + errors);
        }

        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException(
                "Tibber API returnerade data = null. Kontrollera token och scopes.");
        }

        return doc;
    }

    private bool IsWantedHome(string? homeId, string? nickname)
    {
        if (!string.IsNullOrWhiteSpace(_config.PreferredTibberHomeId))
        {
            return string.Equals(
                homeId,
                _config.PreferredTibberHomeId,
                StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(_config.PreferredTibberHomeNickname))
        {
            return string.Equals(
                nickname,
                _config.PreferredTibberHomeNickname,
                StringComparison.OrdinalIgnoreCase);
        }

        return true;
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

    private static void ReadPrices(JsonElement array, List<PriceSlot> result)
    {
        foreach (var item in array.EnumerateArray())
        {
            var startsAtText = item.GetProperty("startsAt").GetString();

            if (string.IsNullOrWhiteSpace(startsAtText))
                continue;

            var startsAt = DateTimeOffset.Parse(startsAtText);
            var total = item.GetProperty("total").GetDecimal();

            result.Add(new PriceSlot(
                startsAt,
                total));
        }
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Null)
            return null;

        return property.GetString();
    }

    private static string? GetAddressOrNull(JsonElement home)
    {
        if (!home.TryGetProperty("address", out var address))
            return null;

        if (address.ValueKind == JsonValueKind.Null)
            return null;

        return GetStringOrNull(address, "address1");
    }
}