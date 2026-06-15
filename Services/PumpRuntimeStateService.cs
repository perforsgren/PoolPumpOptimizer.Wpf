using System.IO;
using System.Text.Json;
using PoolPumpOptimizer.Wpf.Models;

namespace PoolPumpOptimizer.Wpf.Services;

/// <summary>
/// Läser och sparar den lokalt beräknade pumpstatistiken.
/// </summary>
public sealed class PumpRuntimeStateService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public string StateFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoolPumpOptimizer");

    public string StatePath =>
        Path.Combine(StateFolder, "runtime-state.json");

    /// <summary>
    /// Läser tidigare sparad driftstatus. En saknad eller skadad fil ger en
    /// ny tom status i stället för att hindra applikationen från att starta.
    /// </summary>
    public PumpRuntimeState Load()
    {
        if (!File.Exists(StatePath))
            return new PumpRuntimeState();

        try
        {
            var json = File.ReadAllText(StatePath);

            var state = JsonSerializer.Deserialize<PumpRuntimeState>(
                json,
                _jsonOptions);

            return state ?? new PumpRuntimeState();
        }
        catch
        {
            return new PumpRuntimeState();
        }
    }

    /// <summary>
    /// Sparar driftstatus atomiskt för att minska risken för en halvskriven
    /// JSON-fil om programmet avslutas under skrivning.
    /// </summary>
    public void Save(PumpRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Directory.CreateDirectory(StateFolder);

        var json = JsonSerializer.Serialize(state, _jsonOptions);
        var temporaryPath = StatePath + ".tmp";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, StatePath, true);
    }
}
