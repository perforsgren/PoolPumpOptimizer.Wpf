using System.IO;
using System.Text.Json;
using PoolPumpOptimizer.Wpf.Models;

namespace PoolPumpOptimizer.Wpf.Services;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public string SettingsFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoolPumpOptimizer");

    public string SettingsPath =>
        Path.Combine(SettingsFolder, "settings.json");

    public PoolPumpConfig Load()
    {
        if (!File.Exists(SettingsPath))
            return new PoolPumpConfig();

        var json = File.ReadAllText(SettingsPath);

        var config = JsonSerializer.Deserialize<PoolPumpConfig>(
            json,
            _jsonOptions);

        return config ?? new PoolPumpConfig();
    }

    public void Save(PoolPumpConfig config)
    {
        Directory.CreateDirectory(SettingsFolder);

        var json = JsonSerializer.Serialize(config, _jsonOptions);

        File.WriteAllText(SettingsPath, json);
    }
}