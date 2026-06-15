using PoolPumpOptimizer.Wpf.Models;

namespace PoolPumpOptimizer.Wpf.Services;

/// <summary>
/// Beräknar körd tid och antal starter från verifierade Shelly-statusar.
///
/// Endast tidsintervall mellan två statusläsningar som ligger tillräckligt
/// nära varandra räknas som tillförlitliga. Därmed räknas inte timmar då
/// WPF-appen varit stängd automatiskt som drifttid.
/// </summary>
public sealed class PumpRuntimeTracker
{
    private readonly object _sync = new();
    private readonly PumpRuntimeStateService _stateService;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private PumpRuntimeState _state;

    public PumpRuntimeTracker(PumpRuntimeStateService stateService)
    {
        _stateService = stateService
            ?? throw new ArgumentNullException(nameof(stateService));

        _state = _stateService.Load();
        NormalizeLoadedState();
    }

    /// <summary>
    /// Returnerar sparad statistik för vald Shelly-enhet. Om den sparade
    /// statistiken tillhör en annan enhet visas en tom ögonblicksbild.
    /// </summary>
    public PumpRuntimeSnapshot GetSnapshot(
        string deviceId,
        int switchId)
    {
        lock (_sync)
        {
            var normalizedDeviceId = NormalizeDeviceId(deviceId);

            if (!MatchesDevice(normalizedDeviceId, switchId))
            {
                return CreateEmptySnapshot();
            }

            ResetDailyCountersIfNeeded(DateTimeOffset.Now);

            return CreateSnapshot();
        }
    }

    /// <summary>
    /// Returnerar en liveprojektion av dagens körtid. Om pumpen senast
    /// observerades som PÅ i den aktuella appsessionen räknas tiden fram till
    /// nu med i visningen utan att state-filen skrivs varje sekund.
    /// </summary>
    public PumpRuntimeSnapshot GetLiveSnapshot(
        string deviceId,
        int switchId,
        DateTimeOffset now,
        TimeSpan maximumReliableGap)
    {
        lock (_sync)
        {
            var normalizedDeviceId = NormalizeDeviceId(deviceId);

            if (!MatchesDevice(normalizedDeviceId, switchId))
                return CreateEmptySnapshot();

            var today = DateOnly.FromDateTime(now.LocalDateTime);
            var runSeconds = _state.TrackingDate == today
                ? Math.Max(0, _state.RunSecondsToday)
                : 0;

            var startsToday = _state.TrackingDate == today
                ? Math.Max(0, _state.StartsToday)
                : 0;

            if (_state.CurrentState == true &&
                _state.LastObservedAt.HasValue &&
                string.Equals(
                    _state.ObservationSessionId,
                    _sessionId,
                    StringComparison.Ordinal))
            {
                var elapsed = now - _state.LastObservedAt.Value;

                if (elapsed >= TimeSpan.Zero &&
                    elapsed <= maximumReliableGap)
                {
                    var startOfToday = new DateTimeOffset(
                        now.Year,
                        now.Month,
                        now.Day,
                        0,
                        0,
                        0,
                        now.Offset);

                    var intervalStart = _state.LastObservedAt.Value > startOfToday
                        ? _state.LastObservedAt.Value
                        : startOfToday;

                    if (now > intervalStart)
                    {
                        runSeconds +=
                            (now - intervalStart).TotalSeconds;
                    }
                }
            }

            runSeconds = Math.Clamp(
                runSeconds,
                0,
                TimeSpan.FromDays(1).TotalSeconds);

            return new PumpRuntimeSnapshot(
                TrackingDate: today,
                CurrentState: _state.CurrentState,
                RunTimeToday: TimeSpan.FromSeconds(runSeconds),
                StartsToday: startsToday,
                LastStartedAt: _state.LastStartedAt,
                LastStoppedAt: _state.LastStoppedAt,
                LastObservedAt: _state.LastObservedAt);
        }
    }

    /// <summary>
    /// Registrerar en verifierad Shelly-status och returnerar uppdaterad
    /// statistik. Offline-status och status utan känt reläläge ignoreras.
    /// </summary>
    public PumpRuntimeSnapshot Observe(
        ShellySwitchStatus status,
        TimeSpan maximumReliableGap)
    {
        ArgumentNullException.ThrowIfNull(status);

        lock (_sync)
        {
            var normalizedDeviceId = NormalizeDeviceId(status.DeviceId);
            EnsureDevice(normalizedDeviceId, status.SwitchId);

            if (!status.IsOnline || !status.IsOn.HasValue)
                return CreateSnapshot();

            var observedAt = ToLocalOffset(status.ReadAtLocal);
            var observedState = status.IsOn.Value;

            var previousObservedAt = _state.LastObservedAt;
            var previousState = _state.CurrentState;

            var elapsed = previousObservedAt.HasValue
                ? observedAt - previousObservedAt.Value
                : TimeSpan.Zero;

            var isReliableContinuation =
                previousObservedAt.HasValue &&
                string.Equals(
                    _state.ObservationSessionId,
                    _sessionId,
                    StringComparison.Ordinal) &&
                elapsed >= TimeSpan.Zero &&
                elapsed <= maximumReliableGap;

            AddReliableElapsedTime(
                observedAt,
                previousObservedAt,
                previousState,
                isReliableContinuation);

            RegisterReliableTransition(
                observedAt,
                observedState,
                previousState,
                isReliableContinuation);

            _state.CurrentState = observedState;
            _state.LastObservedAt = observedAt;
            _state.ObservationSessionId = _sessionId;

            if (!_state.CurrentStateSince.HasValue ||
                previousState != observedState)
            {
                _state.CurrentStateSince = observedAt;
            }

            _stateService.Save(_state);

            return CreateSnapshot();
        }
    }

    private void AddReliableElapsedTime(
        DateTimeOffset observedAt,
        DateTimeOffset? previousObservedAt,
        bool? previousState,
        bool isReliableContinuation)
    {
        var observedDate = DateOnly.FromDateTime(observedAt.LocalDateTime);

        if (_state.TrackingDate != observedDate)
        {
            _state.TrackingDate = observedDate;
            _state.RunSecondsToday = 0;
            _state.StartsToday = 0;
        }

        if (!isReliableContinuation ||
            previousObservedAt == null ||
            previousState != true)
        {
            return;
        }

        var startOfToday = new DateTimeOffset(
            observedAt.Year,
            observedAt.Month,
            observedAt.Day,
            0,
            0,
            0,
            observedAt.Offset);

        var intervalStart = previousObservedAt.Value > startOfToday
            ? previousObservedAt.Value
            : startOfToday;

        if (observedAt <= intervalStart)
            return;

        _state.RunSecondsToday +=
            (observedAt - intervalStart).TotalSeconds;

        _state.RunSecondsToday = Math.Clamp(
            _state.RunSecondsToday,
            0,
            TimeSpan.FromDays(1).TotalSeconds);
    }

    private void RegisterReliableTransition(
        DateTimeOffset observedAt,
        bool observedState,
        bool? previousState,
        bool isReliableContinuation)
    {
        if (!previousState.HasValue ||
            previousState.Value == observedState ||
            !isReliableContinuation)
        {
            return;
        }

        if (observedState)
        {
            _state.StartsToday++;
            _state.LastStartedAt = observedAt;
        }
        else
        {
            _state.LastStoppedAt = observedAt;
        }
    }

    private void EnsureDevice(
        string normalizedDeviceId,
        int switchId)
    {
        if (MatchesDevice(normalizedDeviceId, switchId))
            return;

        _state = new PumpRuntimeState
        {
            DeviceId = normalizedDeviceId,
            SwitchId = switchId,
            TrackingDate = DateOnly.FromDateTime(DateTime.Now)
        };

        _stateService.Save(_state);
    }

    private bool MatchesDevice(
        string normalizedDeviceId,
        int switchId)
    {
        return
            string.Equals(
                NormalizeDeviceId(_state.DeviceId),
                normalizedDeviceId,
                StringComparison.OrdinalIgnoreCase) &&
            _state.SwitchId == switchId;
    }

    private void ResetDailyCountersIfNeeded(
        DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);

        if (_state.TrackingDate == today)
            return;

        _state.TrackingDate = today;
        _state.RunSecondsToday = 0;
        _state.StartsToday = 0;
        _stateService.Save(_state);
    }

    private void NormalizeLoadedState()
    {
        _state.DeviceId = NormalizeDeviceId(_state.DeviceId);
        _state.RunSecondsToday = Math.Clamp(
            _state.RunSecondsToday,
            0,
            TimeSpan.FromDays(1).TotalSeconds);
        _state.StartsToday = Math.Max(0, _state.StartsToday);
    }

    private PumpRuntimeSnapshot CreateSnapshot()
    {
        return new PumpRuntimeSnapshot(
            TrackingDate: _state.TrackingDate,
            CurrentState: _state.CurrentState,
            RunTimeToday: TimeSpan.FromSeconds(
                Math.Max(0, _state.RunSecondsToday)),
            StartsToday: Math.Max(0, _state.StartsToday),
            LastStartedAt: _state.LastStartedAt,
            LastStoppedAt: _state.LastStoppedAt,
            LastObservedAt: _state.LastObservedAt);
    }

    private static PumpRuntimeSnapshot CreateEmptySnapshot()
    {
        return new PumpRuntimeSnapshot(
            TrackingDate: DateOnly.FromDateTime(DateTime.Now),
            CurrentState: null,
            RunTimeToday: TimeSpan.Zero,
            StartsToday: 0,
            LastStartedAt: null,
            LastStoppedAt: null,
            LastObservedAt: null);
    }

    private static string NormalizeDeviceId(string? deviceId)
    {
        return (deviceId ?? "").Trim().ToUpperInvariant();
    }

    private static DateTimeOffset ToLocalOffset(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return new DateTimeOffset(value).ToLocalTime();

        if (value.Kind == DateTimeKind.Local)
            return new DateTimeOffset(value);

        return new DateTimeOffset(
            DateTime.SpecifyKind(value, DateTimeKind.Local));
    }
}
