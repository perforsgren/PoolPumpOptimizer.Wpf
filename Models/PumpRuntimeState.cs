namespace PoolPumpOptimizer.Wpf.Models;

/// <summary>
/// Beständig lokal driftstatus för poolpumpen.
///
/// Statistiken bygger på verifierade Shelly-statusläsningar medan
/// applikationen körs. Långa perioder utan observation räknas inte som
/// säker driftstid, eftersom pumpen kan ha växlats medan appen varit stängd.
/// </summary>
public sealed class PumpRuntimeState
{
    public string DeviceId { get; set; } = "";

    public int SwitchId { get; set; }

    public DateOnly TrackingDate { get; set; } =
        DateOnly.FromDateTime(DateTime.Now);

    public bool? CurrentState { get; set; }

    public DateTimeOffset? CurrentStateSince { get; set; }

    public DateTimeOffset? LastObservedAt { get; set; }

    /// <summary>
    /// Identifierar den appsession som gjorde den senaste observationen.
    /// Därmed räknas inte tiden då appen varit stängd som säker drifttid.
    /// </summary>
    public string ObservationSessionId { get; set; } = "";

    public double RunSecondsToday { get; set; }

    public int StartsToday { get; set; }

    public DateTimeOffset? LastStartedAt { get; set; }

    public DateTimeOffset? LastStoppedAt { get; set; }
}
