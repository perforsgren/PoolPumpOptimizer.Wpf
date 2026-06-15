using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PoolPumpOptimizer.Wpf.Models;
using PoolPumpOptimizer.Wpf.Services;
using SkiaSharp;

namespace PoolPumpOptimizer.Wpf.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly PumpRuntimeStateService _pumpRuntimeStateService = new();
    private readonly PumpRuntimeTracker _pumpRuntimeTracker;
    private readonly DispatcherTimer _pumpRuntimeUiTimer;
    private readonly DispatcherTimer _tibberPriceRefreshTimer;
    private readonly SemaphoreSlim _tibberPriceRefreshGate = new(1, 1);

    private string? _lastAutomaticPriceRefreshError;

    private PoolPumpConfig _config;
    private List<PriceSlot> _prices = new();
    private PumpPlan? _fullPlan;

    private string _statusText = "Redo.";
    private string _selectedHomeText = "-";
    private string _todayRunText = "-";
    private string _tomorrowRunText = "-";
    private string _todayStartsText = "-";
    private string _averageRunPriceText = "-";
    private string _lastUpdatedTimeText = "-";
    private string _lastUpdatedTooltipText = "Ingen uppdatering ännu.";

    private string _shellyOnlineText = "Inte läst";
    private string _shellyOutputText = "-";
    private string _shellyPowerText = "-";
    private string _shellyVoltageText = "-";
    private string _shellyCurrentText = "-";
    private string _shellyOnTimeText = "00:00:00";
    private string _shellySwitchOnCountText = "0";
    private string _shellyRuntimeTooltipText =
        "Egen statistik har ännu inte fått någon verifierad Shelly-status.";
    private string _shellyLastReadTimeText = "-";
    private string _shellyLastReadTooltipText = "Ingen Shelly-status har lästs ännu.";
    private string _shellyControlModeText = "Stoppad";
    private string _shellyPlanTargetText = "-";
    private string _shellyNextChangeText = "-";
    private string _shellyControlLastActionText = "Ingen automatisk åtgärd ännu.";

    private Brush _lastUpdatedBrush =
        new SolidColorBrush(Color.FromRgb(242, 245, 248));

    private TibberHome? _selectedHome;
    private string _selectedDayFilter = "Alla";

    private decimal _minRunHoursPerDay;
    private decimal _preferredBlockHours;
    private decimal _maxStartsPerDay;
    private decimal _cheapPriceThresholdSekPerKwh;
    private decimal _minExtraCheapBlockMinutes;
    private decimal _minOnMinutes;
    private decimal _minOffMinutes;
    private decimal _shellySwitchId;
    private decimal _shellyControlPollSeconds;

    private bool _isInitialized;
    private bool _isShellyAutomaticControlRunning;
    private CancellationTokenSource? _shellyControlCancellation;
    private readonly SemaphoreSlim _shellyControlGate = new(1, 1);

    private int _shellyAutomaticConsecutiveFailures;
    private string? _lastShellyAutomaticError;
    private DateTime? _lastShellyAutomaticSuccessfulCycleLocal;

    private bool _isPlanTabSelected = true;
    private bool _isDetailsTabSelected;
    private bool _isLogTabSelected;

    /// <summary>
    /// Skapar huvud-vyns ViewModel och laddar sparad konfiguration.
    /// </summary>
    public MainViewModel()
    {
        _config = _settingsService.Load();
        _pumpRuntimeTracker =
            new PumpRuntimeTracker(_pumpRuntimeStateService);

        _pumpRuntimeUiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _pumpRuntimeUiTimer.Tick += (_, _) =>
            RefreshPumpRuntimeDisplay();

        _pumpRuntimeUiTimer.Start();

        _tibberPriceRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };

        _tibberPriceRefreshTimer.Tick +=
            TibberPriceRefreshTimer_Tick;

        _minRunHoursPerDay =
            _config.MinRunHoursPerDay;

        _preferredBlockHours =
            _config.PreferredBlockHours;

        _maxStartsPerDay =
            _config.MaxStartsPerDay;

        _cheapPriceThresholdSekPerKwh =
            _config.CheapPriceThresholdSekPerKwh;

        _minExtraCheapBlockMinutes =
            _config.MinExtraCheapBlockMinutes;

        _minOnMinutes =
            _config.MinOnMinutes;

        _minOffMinutes =
            _config.MinOffMinutes;

        _shellySwitchId =
            _config.ShellySwitchId;

        _shellyControlPollSeconds =
            _config.ShellyControlPollSeconds;

        DayFilters.Add("Alla");
        DayFilters.Add("Idag");
        DayFilters.Add("Imorgon");

        LoadHomesCommand =
            new RelayCommand(LoadHomesAsync);

        FetchPricesCommand =
            new RelayCommand(FetchPricesAsync);

        OptimizeCommand =
            new RelayCommand(OptimizeAsync);

        SaveSettingsCommand =
            new RelayCommand(SaveSettingsAsync);

        LoadSettingsCommand =
            new RelayCommand(LoadSettingsAsync);

        ClearLogCommand =
            new RelayCommand(ClearLogAsync);

        TestShellyConnectionCommand =
            new RelayCommand(TestShellyConnectionAsync);

        ReadShellyStatusCommand =
            new RelayCommand(ReadShellyStatusAsync);

        TurnShellyOnCommand =
            new RelayCommand(TurnShellyOnAsync);

        TurnShellyOffCommand =
            new RelayCommand(TurnShellyOffAsync);

        StartShellyAutomaticControlCommand =
            new RelayCommand(StartShellyAutomaticControlAsync);

        StopShellyAutomaticControlCommand =
            new RelayCommand(StopShellyAutomaticControlAsync);

        PriceSeries = Array.Empty<ISeries>();

        XAxes =
        [
            new Axis
            {
                LabelsRotation = 45,
                TextSize = 11,
                LabelsPaint =
                    new SolidColorPaint(
                        new SKColor(205, 213, 225)),
                SeparatorsPaint =
                    new SolidColorPaint(
                        new SKColor(52, 58, 70))
            }
        ];

        YAxes =
        [
            new Axis
            {
                Name = "SEK/kWh",
                TextSize = 11,
                LabelsPaint =
                    new SolidColorPaint(
                        new SKColor(205, 213, 225)),
                SeparatorsPaint =
                    new SolidColorPaint(
                        new SKColor(52, 58, 70))
            }
        ];

        AddLog(
            "Info",
            "Applikationen startades.");

        ApplyPumpRuntimeSnapshot(
            _pumpRuntimeTracker.GetSnapshot(
                _config.ShellyDeviceId,
                _config.ShellySwitchId));

        AddLog(
            "Info",
            $"Inställningsfil: {_settingsService.SettingsPath}");

        AddLog(
            "Info",
            $"Driftstatusfil: {_pumpRuntimeStateService.StatePath}");
    }

    public ObservableCollection<TibberHome> Homes { get; } = new();

    public ObservableCollection<string> DayFilters { get; } = new();

    public ObservableCollection<RunBlock> RunBlocks { get; } = new();

    public ObservableCollection<PlannedSlot> DetailSlots { get; } = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public RelayCommand LoadHomesCommand { get; }

    public RelayCommand FetchPricesCommand { get; }

    public RelayCommand OptimizeCommand { get; }

    public RelayCommand SaveSettingsCommand { get; }

    public RelayCommand LoadSettingsCommand { get; }

    public RelayCommand ClearLogCommand { get; }

    public RelayCommand TestShellyConnectionCommand { get; }

    public RelayCommand ReadShellyStatusCommand { get; }

    public RelayCommand TurnShellyOnCommand { get; }

    public RelayCommand TurnShellyOffCommand { get; }

    public RelayCommand StartShellyAutomaticControlCommand { get; }

    public RelayCommand StopShellyAutomaticControlCommand { get; }

    public ISeries[] PriceSeries { get; private set; }

    public Axis[] XAxes { get; private set; }

    public Axis[] YAxes { get; private set; }

    public bool IsPlanTabSelected
    {
        get => _isPlanTabSelected;

        set
        {
            if (!SetProperty(
                    ref _isPlanTabSelected,
                    value))
            {
                return;
            }

            if (value)
            {
                IsDetailsTabSelected = false;
                IsLogTabSelected = false;
            }

            OnPropertyChanged(nameof(IsPlanVisible));
            OnPropertyChanged(nameof(IsDetailsVisible));
            OnPropertyChanged(nameof(IsLogVisible));
        }
    }

    public bool IsDetailsTabSelected
    {
        get => _isDetailsTabSelected;

        set
        {
            if (!SetProperty(
                    ref _isDetailsTabSelected,
                    value))
            {
                return;
            }

            if (value)
            {
                IsPlanTabSelected = false;
                IsLogTabSelected = false;
            }

            OnPropertyChanged(nameof(IsPlanVisible));
            OnPropertyChanged(nameof(IsDetailsVisible));
            OnPropertyChanged(nameof(IsLogVisible));
        }
    }

    public bool IsLogTabSelected
    {
        get => _isLogTabSelected;

        set
        {
            if (!SetProperty(
                    ref _isLogTabSelected,
                    value))
            {
                return;
            }

            if (value)
            {
                IsPlanTabSelected = false;
                IsDetailsTabSelected = false;
            }

            OnPropertyChanged(nameof(IsPlanVisible));
            OnPropertyChanged(nameof(IsDetailsVisible));
            OnPropertyChanged(nameof(IsLogVisible));
        }
    }

    public bool IsPlanVisible =>
        IsPlanTabSelected;

    public bool IsDetailsVisible =>
        IsDetailsTabSelected;

    public bool IsLogVisible =>
        IsLogTabSelected;

    public string TibberToken
    {
        get => _config.TibberToken;

        set
        {
            if (_config.TibberToken == value)
                return;

            _config.TibberToken = value;

            OnPropertyChanged();
        }
    }

    public bool UseProxy
    {
        get => _config.UseProxy;

        set
        {
            if (_config.UseProxy == value)
                return;

            _config.UseProxy = value;

            OnPropertyChanged();
        }
    }

    public string ProxyAddress
    {
        get => _config.ProxyAddress ?? "";

        set
        {
            if (_config.ProxyAddress == value)
                return;

            _config.ProxyAddress = value;

            OnPropertyChanged();
        }
    }

    public string ShellyCloudServer
    {
        get => _config.ShellyCloudServer;

        set
        {
            if (_config.ShellyCloudServer == value)
                return;

            _config.ShellyCloudServer = value;
            OnPropertyChanged();
        }
    }

    public string ShellyCloudAuthKey
    {
        get => _config.ShellyCloudAuthKey;

        set
        {
            if (_config.ShellyCloudAuthKey == value)
                return;

            _config.ShellyCloudAuthKey = value;
            OnPropertyChanged();
        }
    }

    public string ShellyDeviceId
    {
        get => _config.ShellyDeviceId;

        set
        {
            if (_config.ShellyDeviceId == value)
                return;

            _config.ShellyDeviceId = value;
            OnPropertyChanged();
        }
    }

    public decimal ShellySwitchId
    {
        get => _shellySwitchId;

        set
        {
            var clamped = Clamp(value, 0m, 99m);

            if (!SetProperty(ref _shellySwitchId, clamped))
                return;
        }
    }

    public bool ShellyManualControlEnabled
    {
        get => _config.ShellyManualControlEnabled;

        set
        {
            if (_config.ShellyManualControlEnabled == value)
                return;

            _config.ShellyManualControlEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool ShellyAutomaticControlEnabled
    {
        get => _config.ShellyAutomaticControlEnabled;

        set
        {
            if (_config.ShellyAutomaticControlEnabled == value)
                return;

            _config.ShellyAutomaticControlEnabled = value;
            OnPropertyChanged();
        }
    }

    public decimal ShellyControlPollSeconds
    {
        get => _shellyControlPollSeconds;

        set
        {
            var clamped = Clamp(value, 15m, 300m);

            if (!SetProperty(ref _shellyControlPollSeconds, clamped))
                return;
        }
    }

    public decimal MinRunHoursPerDay
    {
        get => _minRunHoursPerDay;

        set
        {
            var clamped =
                Clamp(value, 1m, 24m);

            if (!SetProperty(
                    ref _minRunHoursPerDay,
                    clamped))
            {
                return;
            }

            ReoptimizeIfPossible();
        }
    }

    public decimal PreferredBlockHours
    {
        get => _preferredBlockHours;

        set
        {
            var clamped =
                Clamp(value, 1m, 24m);

            if (!SetProperty(
                    ref _preferredBlockHours,
                    clamped))
            {
                return;
            }

            ReoptimizeIfPossible();
        }
    }

    public decimal MaxStartsPerDay
    {
        get => _maxStartsPerDay;

        set
        {
            var clamped =
                Clamp(value, 1m, 24m);

            if (!SetProperty(
                    ref _maxStartsPerDay,
                    clamped))
            {
                return;
            }

            ReoptimizeIfPossible();
        }
    }

    public decimal CheapPriceThresholdSekPerKwh
    {
        get => _cheapPriceThresholdSekPerKwh;

        set
        {
            var clamped =
                Clamp(value, -10m, 20m);

            if (!SetProperty(
                    ref _cheapPriceThresholdSekPerKwh,
                    clamped))
            {
                return;
            }

            ReoptimizeIfPossible();
        }
    }

    /// <summary>
    /// Minsta sammanhängande extra billiga period som får
    /// skapa ett nytt fristående körblock.
    /// </summary>
    public decimal MinExtraCheapBlockMinutes
    {
        get => _minExtraCheapBlockMinutes;

        set
        {
            var clamped =
                Clamp(value, 15m, 1440m);

            if (!SetProperty(
                    ref _minExtraCheapBlockMinutes,
                    clamped))
            {
                return;
            }

            ReoptimizeIfPossible();
        }
    }

    public decimal MinOnMinutes
    {
        get => _minOnMinutes;

        set
        {
            var clamped =
                Clamp(value, 15m, 1440m);

            if (!SetProperty(
                    ref _minOnMinutes,
                    clamped))
            {
                return;
            }

            ReoptimizeIfPossible();
        }
    }

    public decimal MinOffMinutes
    {
        get => _minOffMinutes;

        set
        {
            var clamped =
                Clamp(value, 15m, 1440m);

            if (!SetProperty(
                    ref _minOffMinutes,
                    clamped))
            {
                return;
            }

            ReoptimizeIfPossible();
        }
    }

    public bool IgnorePastSlots
    {
        get => _config.IgnorePastSlots;

        set
        {
            if (_config.IgnorePastSlots == value)
                return;

            _config.IgnorePastSlots = value;

            OnPropertyChanged();

            ReoptimizeIfPossible();
        }
    }

    public TibberHome? SelectedHome
    {
        get => _selectedHome;

        set
        {
            if (!SetProperty(
                    ref _selectedHome,
                    value))
            {
                return;
            }

            _config.PreferredTibberHomeId =
                value?.Id;

            _config.PreferredTibberHomeNickname =
                value?.AppNickname;

            SelectedHomeText =
                value?.NicknameText ?? "-";

            if (_isInitialized && value != null)
            {
                SaveSelectedHomeQuietly();

                AddLog(
                    "Info",
                    $"Valt home: {value.NicknameText}");
            }
        }
    }

    public string SelectedDayFilter
    {
        get => _selectedDayFilter;

        set
        {
            if (!SetProperty(
                    ref _selectedDayFilter,
                    value))
            {
                return;
            }

            RefreshVisiblePlan();
        }
    }

    public string StatusText
    {
        get => _statusText;

        private set =>
            SetProperty(
                ref _statusText,
                value);
    }

    public string SelectedHomeText
    {
        get => _selectedHomeText;

        private set =>
            SetProperty(
                ref _selectedHomeText,
                value);
    }

    public string TodayRunText
    {
        get => _todayRunText;

        private set =>
            SetProperty(
                ref _todayRunText,
                value);
    }

    public string TomorrowRunText
    {
        get => _tomorrowRunText;

        private set =>
            SetProperty(
                ref _tomorrowRunText,
                value);
    }

    public string TodayStartsText
    {
        get => _todayStartsText;

        private set =>
            SetProperty(
                ref _todayStartsText,
                value);
    }

    public string AverageRunPriceText
    {
        get => _averageRunPriceText;

        private set =>
            SetProperty(
                ref _averageRunPriceText,
                value);
    }

    public string LastUpdatedTimeText
    {
        get => _lastUpdatedTimeText;

        private set =>
            SetProperty(
                ref _lastUpdatedTimeText,
                value);
    }

    public string LastUpdatedTooltipText
    {
        get => _lastUpdatedTooltipText;

        private set =>
            SetProperty(
                ref _lastUpdatedTooltipText,
                value);
    }

    public Brush LastUpdatedBrush
    {
        get => _lastUpdatedBrush;

        private set =>
            SetProperty(
                ref _lastUpdatedBrush,
                value);
    }

    public string ShellyOnlineText
    {
        get => _shellyOnlineText;
        private set => SetProperty(ref _shellyOnlineText, value);
    }

    public string ShellyOutputText
    {
        get => _shellyOutputText;
        private set => SetProperty(ref _shellyOutputText, value);
    }

    public string ShellyPowerText
    {
        get => _shellyPowerText;
        private set => SetProperty(ref _shellyPowerText, value);
    }

    public string ShellyVoltageText
    {
        get => _shellyVoltageText;
        private set => SetProperty(ref _shellyVoltageText, value);
    }

    public string ShellyCurrentText
    {
        get => _shellyCurrentText;
        private set => SetProperty(ref _shellyCurrentText, value);
    }

    public string ShellyOnTimeText
    {
        get => _shellyOnTimeText;
        private set => SetProperty(ref _shellyOnTimeText, value);
    }

    public string ShellySwitchOnCountText
    {
        get => _shellySwitchOnCountText;
        private set => SetProperty(ref _shellySwitchOnCountText, value);
    }

    public string ShellyRuntimeTooltipText
    {
        get => _shellyRuntimeTooltipText;
        private set => SetProperty(ref _shellyRuntimeTooltipText, value);
    }

    public string ShellyLastReadTimeText
    {
        get => _shellyLastReadTimeText;
        private set => SetProperty(ref _shellyLastReadTimeText, value);
    }

    public string ShellyLastReadTooltipText
    {
        get => _shellyLastReadTooltipText;
        private set => SetProperty(ref _shellyLastReadTooltipText, value);
    }

    public string ShellyControlModeText
    {
        get => _shellyControlModeText;
        private set => SetProperty(ref _shellyControlModeText, value);
    }

    public string ShellyPlanTargetText
    {
        get => _shellyPlanTargetText;
        private set => SetProperty(ref _shellyPlanTargetText, value);
    }

    public string ShellyNextChangeText
    {
        get => _shellyNextChangeText;
        private set => SetProperty(ref _shellyNextChangeText, value);
    }

    public string ShellyControlLastActionText
    {
        get => _shellyControlLastActionText;
        private set => SetProperty(ref _shellyControlLastActionText, value);
    }

    public bool IsShellyAutomaticControlRunning
    {
        get => _isShellyAutomaticControlRunning;
        private set
        {
            if (!SetProperty(ref _isShellyAutomaticControlRunning, value))
                return;

            ShellyControlModeText = value ? "Automatik aktiv" : "Stoppad";
        }
    }

    /// <summary>
    /// Initierar applikationen efter att huvudfönstret har laddats.
    /// Om en Tibber-token finns laddas Tibber homes automatiskt.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        _isInitialized = true;

        var initializedSomething = false;

        if (!string.IsNullOrWhiteSpace(TibberToken))
        {
            SetStatus(
                "Token hittad. Laddar Tibber homes och priser automatiskt...",
                "Info");

            await LoadHomesAsync();

            await RefreshTibberPricesAsync(
                isAutomatic: true,
                forceOptimization: true);

            _tibberPriceRefreshTimer.Start();
            initializedSomething = true;
        }

        if (HasShellyConfiguration())
        {
            await ReadShellyStatusAsync();
            initializedSomething = true;
        }

        if (!initializedSomething)
        {
            SetStatus(
                "Redo. Ange Tibber- och Shelly-inställningar i Inställningar.",
                "Info");
        }
    }

    /// <summary>
    /// Hämtar tillgängliga Tibber homes och återställer tidigare valt home.
    /// </summary>
    private async Task LoadHomesAsync()
    {
        try
        {
            SetStatus(
                "Laddar Tibber homes...",
                "Info");

            UpdateConfigFromUi();

            var client =
                new TibberClient(_config);

            var homes =
                await client.GetHomesAsync();

            Homes.Clear();

            foreach (var home in homes)
            {
                Homes.Add(home);
            }

            SelectedHome =
                Homes.FirstOrDefault(
                    x => x.Id ==
                         _config.PreferredTibberHomeId)
                ?? Homes.FirstOrDefault(
                    x => string.Equals(
                        x.AppNickname,
                        _config.PreferredTibberHomeNickname,
                        StringComparison.OrdinalIgnoreCase))
                ?? Homes.FirstOrDefault(
                    x => x.HasCurrentSubscription)
                ?? Homes.FirstOrDefault();

            SetStatus(
                $"Laddade {Homes.Count} Tibber home(s).",
                "OK");
        }
        catch (Exception ex)
        {
            SetStatus(
                ex.Message,
                "Fel");
        }
    }

    /// <summary>
    /// Hämtar kvartstidspriser från Tibber och kör därefter optimeringen.
    /// </summary>
    private async Task FetchPricesAsync()
    {
        await RefreshTibberPricesAsync(
            isAutomatic: false,
            forceOptimization: true);
    }

    /// <summary>
    /// Körs regelbundet medan applikationen är öppen och kontrollerar
    /// om Tibber har publicerat nya eller ändrade prisrader.
    /// </summary>
    private async void TibberPriceRefreshTimer_Tick(
        object? sender,
        EventArgs e)
    {
        await RefreshTibberPricesAsync(
            isAutomatic: true,
            forceOptimization: false);
    }

    /// <summary>
    /// Hämtar priser från Tibber. Vid automatisk kontroll byggs planen
    /// endast om när prisunderlaget faktiskt har ändrats.
    /// </summary>
    private async Task RefreshTibberPricesAsync(
        bool isAutomatic,
        bool forceOptimization)
    {
        if (!await _tibberPriceRefreshGate.WaitAsync(0))
            return;

        try
        {
            if (string.IsNullOrWhiteSpace(TibberToken))
            {
                if (!isAutomatic)
                {
                    SetStatus(
                        "Tibber-token saknas.",
                        "Varning");
                }

                return;
            }

            if (!isAutomatic)
            {
                SetStatus(
                    "Hämtar Tibber-priser...",
                    "Info");
            }

            UpdateConfigFromUi();

            var client =
                new TibberClient(_config);

            var fetchedPrices =
                await client.GetQuarterPricesAsync();

            var pricesChanged =
                !ArePriceListsEqual(_prices, fetchedPrices);

            _lastAutomaticPriceRefreshError = null;
            SetLastUpdated(DateTime.Now);

            if (pricesChanged)
            {
                var oldCount = _prices.Count;
                _prices = fetchedPrices;

                await OptimizeAsync();

                var message = oldCount == 0
                    ? $"Hämtade {_prices.Count} prisrader automatiskt."
                    : $"Nya Tibber-priser upptäcktes. Prisrader: {oldCount} → {_prices.Count}. Planen har optimerats om.";

                SetStatus(
                    message,
                    "OK");
            }
            else if (forceOptimization)
            {
                _prices = fetchedPrices;
                await OptimizeAsync();

                SetStatus(
                    $"Priserna är aktuella. {_prices.Count} prisrader laddade.",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            if (!isAutomatic)
            {
                SetStatus(
                    ex.Message,
                    "Fel");
            }
            else if (!string.Equals(
                         _lastAutomaticPriceRefreshError,
                         ex.Message,
                         StringComparison.Ordinal))
            {
                _lastAutomaticPriceRefreshError = ex.Message;

                AddLog(
                    "Fel",
                    $"Automatisk Tibber-hämtning misslyckades: {ex.Message}");
            }
        }
        finally
        {
            _tibberPriceRefreshGate.Release();
        }
    }

    /// <summary>
    /// Jämför två prislistor på tid och pris så att även korrigerade
    /// priser för redan publicerade kvartsslots upptäcks.
    /// </summary>
    private static bool ArePriceListsEqual(
        IReadOnlyList<PriceSlot> existingPrices,
        IReadOnlyList<PriceSlot> fetchedPrices)
    {
        if (existingPrices.Count != fetchedPrices.Count)
            return false;

        var existingOrdered = existingPrices
            .OrderBy(x => x.StartsAt)
            .ToList();

        var fetchedOrdered = fetchedPrices
            .OrderBy(x => x.StartsAt)
            .ToList();

        for (var index = 0; index < existingOrdered.Count; index++)
        {
            if (existingOrdered[index].StartsAt !=
                fetchedOrdered[index].StartsAt)
            {
                return false;
            }

            if (existingOrdered[index].PriceSekPerKwh !=
                fetchedOrdered[index].PriceSekPerKwh)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Kör om optimeringen med aktuella priser och UI-inställningar.
    /// </summary>
    private Task OptimizeAsync()
    {
        try
        {
            if (_prices.Count == 0)
            {
                SetStatus(
                    "Inga priser laddade. Hämta priser först.",
                    "Varning");

                return Task.CompletedTask;
            }

            UpdateConfigFromUi();

            var optimizer =
                new Services.PoolPumpOptimizer(_config);

            _fullPlan =
                optimizer.BuildPlan(_prices);

            RefreshVisiblePlan();
            UpdateSummary();

            SetStatus(
                "Optimering klar.",
                "OK");
        }
        catch (Exception ex)
        {
            SetStatus(
                ex.Message,
                "Fel");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sparar aktuell konfiguration till settings.json.
    /// </summary>
    private Task SaveSettingsAsync()
    {
        try
        {
            UpdateConfigFromUi();

            _settingsService.Save(_config);

            SetStatus(
                $"Inställningar sparade: {_settingsService.SettingsPath}",
                "OK");
        }
        catch (Exception ex)
        {
            SetStatus(
                ex.Message,
                "Fel");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Läser om konfigurationen från settings.json.
    /// </summary>
    private Task LoadSettingsAsync()
    {
        try
        {
            var loadedConfig =
                _settingsService.Load();

            ApplyLoadedConfig(loadedConfig);

            if (!string.IsNullOrWhiteSpace(TibberToken))
            {
                _tibberPriceRefreshTimer.Start();
            }
            else
            {
                _tibberPriceRefreshTimer.Stop();
            }

            SelectedHome =
                Homes.FirstOrDefault(
                    x => x.Id ==
                         _config.PreferredTibberHomeId)
                ?? SelectedHome;

            SetStatus(
                "Inställningar laddade.",
                "OK");
        }
        catch (Exception ex)
        {
            SetStatus(
                ex.Message,
                "Fel");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Testar anslutningen till Shelly Cloud genom att läsa enhetsstatus.
    /// </summary>
    private async Task TestShellyConnectionAsync()
    {
        try
        {
            SetStatus(
                "Testar anslutningen till Shelly Cloud...",
                "Info");

            UpdateConfigFromUi();

            using var client =
                new ShellyCloudClient(_config);

            var shellyStatus =
                await client.TestConnectionAsync();

            ApplyShellyStatus(shellyStatus);

            SetStatus(
                shellyStatus.IsOnline
                    ? "Anslutningen till Shelly Cloud fungerar. Enheten är online."
                    : "Anslutningen till Shelly Cloud fungerar, men enheten är offline.",
                shellyStatus.IsOnline ? "OK" : "Varning");
        }
        catch (Exception ex)
        {
            SetStatus(
                $"Shelly-testet misslyckades: {ex.Message}",
                "Fel");
        }
    }

    /// <summary>
    /// Läser aktuell status för vald Shelly-switch.
    /// </summary>
    private async Task ReadShellyStatusAsync()
    {
        try
        {
            SetStatus(
                "Läser Shelly-status...",
                "Info");

            UpdateConfigFromUi();

            using var client =
                new ShellyCloudClient(_config);

            var shellyStatus =
                await client.GetSwitchStatusAsync();

            ApplyShellyStatus(shellyStatus);

            SetStatus(
                shellyStatus.IsOnline
                    ? "Shelly-status läst."
                    : "Shelly-status läst. Enheten är offline.",
                shellyStatus.IsOnline ? "OK" : "Varning");
        }
        catch (Exception ex)
        {
            SetStatus(
                $"Kunde inte läsa Shelly-status: {ex.Message}",
                "Fel");
        }
    }

    /// <summary>
    /// Rensar statusloggen.
    /// </summary>
    private Task ClearLogAsync()
    {
        LogEntries.Clear();

        AddLog(
            "Info",
            "Loggen rensades.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Slår på poolpumpens Shelly-switch och verifierar resultatet.
    /// </summary>
    private Task TurnShellyOnAsync()
    {
        return SetShellySwitchStateAsync(true);
    }

    /// <summary>
    /// Slår av poolpumpens Shelly-switch och verifierar resultatet.
    /// </summary>
    private Task TurnShellyOffAsync()
    {
        return SetShellySwitchStateAsync(false);
    }

    /// <summary>
    /// Skickar ett explicit PÅ- eller AV-kommando till Shelly Cloud.
    /// Toggle används aldrig eftersom det kan ge fel slutläge om statusen är gammal.
    /// </summary>
    private async Task SetShellySwitchStateAsync(bool turnOn)
    {
        try
        {
            UpdateConfigFromUi();

            if (!ShellyManualControlEnabled)
            {
                throw new InvalidOperationException(
                    "Manuell Shelly-styrning är inte aktiverad. " +
                    "Aktivera den i Inställningar och spara först.");
            }

            if (IsShellyAutomaticControlRunning)
            {
                throw new InvalidOperationException(
                    "Stoppa automatisk styrning innan ett manuellt kommando skickas.");
            }

            var targetText = turnOn ? "PÅ" : "AV";

            SetStatus(
                $"Skickar Shelly-kommando: {targetText}...",
                "Info");

            using var client = new ShellyCloudClient(_config);

            var verifiedStatus =
                await client.SetSwitchStateAsync(turnOn);

            ApplyShellyStatus(verifiedStatus);

            SetStatus(
                $"Shelly-kommandot verifierades. Pumpen är {targetText}.",
                "OK");
        }
        catch (Exception ex)
        {
            SetStatus(
                $"Shelly-styrningen misslyckades: {ex.Message}",
                "Fel");
        }
    }

    /// <summary>
    /// Startar automatisk Shelly-styrning enligt den aktuella optimeringsplanen.
    /// Funktionen måste startas manuellt efter varje programstart.
    /// </summary>
    private Task StartShellyAutomaticControlAsync()
    {
        try
        {
            UpdateConfigFromUi();

            if (IsShellyAutomaticControlRunning)
            {
                SetStatus(
                    "Automatisk Shelly-styrning är redan aktiv.",
                    "Info");

                return Task.CompletedTask;
            }

            if (!ShellyAutomaticControlEnabled)
            {
                throw new InvalidOperationException(
                    "Automatisk Shelly-styrning är inte tillåten. " +
                    "Aktivera den i Inställningar och spara först.");
            }

            if (_fullPlan == null || _fullPlan.Slots.Count == 0)
            {
                throw new InvalidOperationException(
                    "Ingen optimeringsplan finns. Hämta priser och optimera först.");
            }

            if (!HasShellyConfiguration())
            {
                throw new InvalidOperationException(
                    "Shelly Cloud-inställningarna är inte kompletta.");
            }

            _shellyControlCancellation?.Cancel();
            _shellyControlCancellation?.Dispose();
            _shellyControlCancellation = new CancellationTokenSource();

            _shellyAutomaticConsecutiveFailures = 0;
            _lastShellyAutomaticError = null;
            _lastShellyAutomaticSuccessfulCycleLocal = null;

            IsShellyAutomaticControlRunning = true;
            ShellyControlLastActionText =
                $"Startad {DateTime.Now:HH:mm:ss}.";

            SetStatus(
                "Automatisk Shelly-styrning startad.",
                "OK");

            _ = RunShellyAutomaticControlLoopAsync(
                _shellyControlCancellation.Token);
        }
        catch (Exception ex)
        {
            SetStatus(
                $"Kunde inte starta automatisk styrning: {ex.Message}",
                "Fel");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stoppar den automatiska Shelly-styrningen. Pumpens aktuella läge ändras inte.
    /// </summary>
    private Task StopShellyAutomaticControlAsync()
    {
        _shellyControlCancellation?.Cancel();
        IsShellyAutomaticControlRunning = false;
        ShellyControlLastActionText =
            $"Stoppad {DateTime.Now:HH:mm:ss}. Pumpens läge ändrades inte.";

        SetStatus(
            "Automatisk Shelly-styrning stoppad. Pumpens läge ändrades inte.",
            "Info");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Kör kontrollcykler tills användaren stoppar automatiken eller applikationen avslutas.
    /// Ett tillfälligt kommunikationsfel stoppar inte automatiken. Misslyckade
    /// cykler loggas och följs av en gradvis längre väntetid innan nästa försök.
    /// </summary>
    private async Task RunShellyAutomaticControlLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var cycleSucceeded = false;

                try
                {
                    await ExecuteShellyAutomaticControlCycleAsync(
                        cancellationToken);

                    cycleSucceeded = true;
                    _lastShellyAutomaticSuccessfulCycleLocal =
                        DateTime.Now;

                    if (_shellyAutomaticConsecutiveFailures > 0)
                    {
                        AddLog(
                            "OK",
                            $"Shelly-kommunikationen återställdes efter " +
                            $"{_shellyAutomaticConsecutiveFailures} misslyckade försök.");
                    }

                    _shellyAutomaticConsecutiveFailures = 0;
                    _lastShellyAutomaticError = null;
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _shellyAutomaticConsecutiveFailures++;

                    var errorText = ex.Message.Trim();
                    var retryDelay = GetShellyAutomaticRetryDelay(
                        _shellyAutomaticConsecutiveFailures);

                    ShellyControlLastActionText =
                        $"Kommunikationsfel {DateTime.Now:HH:mm:ss}. " +
                        $"Försök {_shellyAutomaticConsecutiveFailures}; " +
                        $"nytt försök om {FormatRetryDelay(retryDelay)}.";

                    StatusText =
                        "Automatik aktiv – Shelly-kommunikationsfel, " +
                        $"försöker igen om {FormatRetryDelay(retryDelay)}.";

                    if (!string.Equals(
                            _lastShellyAutomaticError,
                            errorText,
                            StringComparison.Ordinal))
                    {
                        AddLog(
                            "Varning",
                            "Automatisk Shelly-kontroll misslyckades, " +
                            $"men automatiken fortsätter: {errorText}");

                        _lastShellyAutomaticError = errorText;
                    }
                    else if (
                        _shellyAutomaticConsecutiveFailures == 3 ||
                        _shellyAutomaticConsecutiveFailures % 10 == 0)
                    {
                        AddLog(
                            "Varning",
                            $"Shelly-felet kvarstår efter " +
                            $"{_shellyAutomaticConsecutiveFailures} försök: " +
                            errorText);
                    }

                    try
                    {
                        await Task.Delay(
                            retryDelay,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (!cycleSucceeded ||
                    cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                var pollSeconds = Math.Clamp(
                    (int)Math.Round(
                        ShellyControlPollSeconds,
                        MidpointRounding.AwayFromZero),
                    15,
                    300);

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(pollSeconds),
                        cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            IsShellyAutomaticControlRunning = false;
        }
    }

    /// <summary>
    /// Returnerar väntetid efter ett misslyckat automatikförsök.
    /// Väntetiden ökar stegvis men begränsas till fem minuter.
    /// </summary>
    private static TimeSpan GetShellyAutomaticRetryDelay(
        int consecutiveFailures)
    {
        var seconds = consecutiveFailures switch
        {
            <= 1 => 30,
            2 => 60,
            3 => 120,
            4 => 180,
            _ => 300
        };

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Formaterar väntetiden för statusfält och logg.
    /// </summary>
    private static string FormatRetryDelay(
        TimeSpan delay)
    {
        return delay.TotalMinutes >= 1
            ? $"{delay.TotalMinutes:0} min"
            : $"{delay.TotalSeconds:0} sek";
    }

    /// <summary>
    /// Jämför planerat läge för aktuell kvart med Shellys verkliga läge och
    /// korrigerar endast när de skiljer sig. Saknas aktuell planslot görs ingenting.
    /// </summary>
    private async Task ExecuteShellyAutomaticControlCycleAsync(
        CancellationToken cancellationToken)
    {
        if (!await _shellyControlGate.WaitAsync(0, cancellationToken))
            return;

        try
        {
            var now = DateTimeOffset.Now;
            var currentSlot = GetCurrentPlannedSlot(now);

            if (currentSlot == null)
            {
                ShellyPlanTargetText = "Ingen planslot";
                ShellyNextChangeText = "-";
                ShellyControlLastActionText =
                    $"Ingen aktuell planslot {DateTime.Now:HH:mm:ss}; ingen styrning utförd.";
                return;
            }

            var desiredOn = currentSlot.Run;
            ShellyPlanTargetText = desiredOn ? "PÅ" : "AV";
            ShellyNextChangeText = GetNextPlanChangeText(currentSlot);

            using var client = new ShellyCloudClient(_config);
            var status = await client.GetSwitchStatusAsync();
            ApplyShellyStatus(status);

            if (!status.IsOnline)
            {
                ShellyControlLastActionText =
                    $"Enheten offline {DateTime.Now:HH:mm:ss}.";
                AddLog(
                    "Varning",
                    "Automatiken kunde inte styra eftersom Shelly-enheten är offline.");
                return;
            }

            if (status.IsOn == desiredOn)
            {
                ShellyControlLastActionText =
                    $"Rätt läge verifierat {DateTime.Now:HH:mm:ss}.";
                return;
            }

            var verifiedStatus = await client.SetSwitchStateAsync(
                desiredOn,
                status);

            ApplyShellyStatus(verifiedStatus);

            var targetText = desiredOn ? "PÅ" : "AV";
            ShellyControlLastActionText =
                $"Växlade till {targetText} {DateTime.Now:HH:mm:ss}.";

            AddLog(
                "OK",
                $"Automatiken växlade pumpen till {targetText} enligt planen.");
        }
        finally
        {
            _shellyControlGate.Release();
        }
    }

    /// <summary>
    /// Hämtar den planslot som täcker aktuell lokal tid.
    /// </summary>
    private PlannedSlot? GetCurrentPlannedSlot(DateTimeOffset now)
    {
        if (_fullPlan == null)
            return null;

        return _fullPlan.Slots
            .OrderBy(x => x.StartsAt)
            .FirstOrDefault(x =>
                now >= x.StartsAt &&
                now < x.StartsAt.AddMinutes(15));
    }

    /// <summary>
    /// Returnerar tidpunkten för nästa planerade ändring efter aktuell slot.
    /// </summary>
    private string GetNextPlanChangeText(PlannedSlot currentSlot)
    {
        if (_fullPlan == null)
            return "-";

        var nextChange = _fullPlan.Slots
            .Where(x => x.StartsAt > currentSlot.StartsAt)
            .OrderBy(x => x.StartsAt)
            .FirstOrDefault(x => x.Run != currentSlot.Run);

        return nextChange == null
            ? "Ingen i planen"
            : nextChange.StartsAt.ToLocalTime().ToString("ddd HH:mm");
    }

    /// <summary>
    /// Applicerar en inläst konfiguration och uppdaterar alla UI-properties.
    /// </summary>
    private void ApplyLoadedConfig(
        PoolPumpConfig loadedConfig)
    {
        _config = loadedConfig;

        OnPropertyChanged(nameof(TibberToken));
        OnPropertyChanged(nameof(UseProxy));
        OnPropertyChanged(nameof(ProxyAddress));
        OnPropertyChanged(nameof(IgnorePastSlots));
        OnPropertyChanged(nameof(ShellyCloudServer));
        OnPropertyChanged(nameof(ShellyCloudAuthKey));
        OnPropertyChanged(nameof(ShellyDeviceId));
        OnPropertyChanged(nameof(ShellyManualControlEnabled));
        OnPropertyChanged(nameof(ShellyAutomaticControlEnabled));

        ShellySwitchId =
            _config.ShellySwitchId;

        ShellyControlPollSeconds =
            _config.ShellyControlPollSeconds;

        ApplyPumpRuntimeSnapshot(
            _pumpRuntimeTracker.GetSnapshot(
                _config.ShellyDeviceId,
                _config.ShellySwitchId));

        MinRunHoursPerDay =
            _config.MinRunHoursPerDay;

        PreferredBlockHours =
            _config.PreferredBlockHours;

        MaxStartsPerDay =
            _config.MaxStartsPerDay;

        CheapPriceThresholdSekPerKwh =
            _config.CheapPriceThresholdSekPerKwh;

        MinExtraCheapBlockMinutes =
            _config.MinExtraCheapBlockMinutes;

        MinOnMinutes =
            _config.MinOnMinutes;

        MinOffMinutes =
            _config.MinOffMinutes;
    }

    /// <summary>
    /// Sparar valt Tibber home utan att ändra huvudstatusen i UI:t.
    /// </summary>
    private void SaveSelectedHomeQuietly()
    {
        try
        {
            UpdateConfigFromUi();

            _settingsService.Save(_config);
        }
        catch (Exception ex)
        {
            AddLog(
                "Fel",
                $"Kunde inte spara valt home: {ex.Message}");
        }
    }

    /// <summary>
    /// Kopierar aktuella UI-värden till konfigurationsmodellen.
    /// </summary>
    private void UpdateConfigFromUi()
    {
        _config.MinRunHoursPerDay =
            (int)Math.Round(
                MinRunHoursPerDay,
                MidpointRounding.AwayFromZero);

        _config.PreferredBlockHours =
            (int)Math.Round(
                PreferredBlockHours,
                MidpointRounding.AwayFromZero);

        _config.MaxStartsPerDay =
            (int)Math.Round(
                MaxStartsPerDay,
                MidpointRounding.AwayFromZero);

        _config.CheapPriceThresholdSekPerKwh =
            CheapPriceThresholdSekPerKwh;

        _config.MinExtraCheapBlockMinutes =
            (int)Math.Round(
                MinExtraCheapBlockMinutes,
                MidpointRounding.AwayFromZero);

        _config.MinOnMinutes =
            (int)Math.Round(
                MinOnMinutes,
                MidpointRounding.AwayFromZero);

        _config.MinOffMinutes =
            (int)Math.Round(
                MinOffMinutes,
                MidpointRounding.AwayFromZero);

        _config.PreferredTibberHomeId =
            SelectedHome?.Id
            ?? _config.PreferredTibberHomeId;

        _config.PreferredTibberHomeNickname =
            SelectedHome?.AppNickname
            ?? _config.PreferredTibberHomeNickname;

        _config.ShellyCloudServer =
            ShellyCloudServer?.Trim() ?? "";

        _config.ShellyCloudAuthKey =
            ShellyCloudAuthKey?.Trim() ?? "";

        _config.ShellyDeviceId =
            ShellyDeviceId?.Trim() ?? "";

        _config.ShellySwitchId =
            (int)Math.Round(
                ShellySwitchId,
                MidpointRounding.AwayFromZero);

        _config.ShellyControlPollSeconds =
            (int)Math.Round(
                ShellyControlPollSeconds,
                MidpointRounding.AwayFromZero);

        ValidateConfig();
    }

    /// <summary>
    /// Begränsar konfigurationsvärden till tillåtna intervall.
    /// </summary>
    private void ValidateConfig()
    {
        _config.MinRunHoursPerDay =
            Math.Clamp(
                _config.MinRunHoursPerDay,
                1,
                24);

        _config.PreferredBlockHours =
            Math.Clamp(
                _config.PreferredBlockHours,
                1,
                24);

        _config.MaxStartsPerDay =
            Math.Clamp(
                _config.MaxStartsPerDay,
                1,
                24);

        _config.CheapPriceThresholdSekPerKwh =
            Math.Clamp(
                _config.CheapPriceThresholdSekPerKwh,
                -10m,
                20m);

        _config.MinExtraCheapBlockMinutes =
            Math.Clamp(
                _config.MinExtraCheapBlockMinutes,
                15,
                1440);

        _config.MinOnMinutes =
            Math.Clamp(
                _config.MinOnMinutes,
                15,
                1440);

        _config.MinOffMinutes =
            Math.Clamp(
                _config.MinOffMinutes,
                15,
                1440);

        _config.ShellySwitchId =
            Math.Clamp(
                _config.ShellySwitchId,
                0,
                99);

        _config.ShellyControlPollSeconds =
            Math.Clamp(
                _config.ShellyControlPollSeconds,
                15,
                300);
    }

    /// <summary>
    /// Kör automatiskt om optimeringen när ett optimeringsvärde ändras.
    /// </summary>
    private void ReoptimizeIfPossible()
    {
        if (_prices.Count == 0)
            return;

        try
        {
            UpdateConfigFromUi();

            var optimizer =
                new Services.PoolPumpOptimizer(_config);

            _fullPlan =
                optimizer.BuildPlan(_prices);

            RefreshVisiblePlan();
            UpdateSummary();

            StatusText =
                "Optimerade automatiskt.";
        }
        catch (Exception ex)
        {
            SetStatus(
                ex.Message,
                "Fel");
        }
    }

    /// <summary>
    /// Uppdaterar graf och tabeller utifrån valt dagfilter.
    /// </summary>
    private void RefreshVisiblePlan()
    {
        if (_fullPlan == null)
            return;

        var visiblePlan =
            GetVisiblePlan();

        UpdateTables(visiblePlan);
        UpdateChart(visiblePlan);
    }

    /// <summary>
    /// Returnerar den del av planen som motsvarar valt dagfilter.
    /// </summary>
    private PumpPlan GetVisiblePlan()
    {
        if (_fullPlan == null)
        {
            return new PumpPlan
            {
                Slots = new List<PlannedSlot>()
            };
        }

        var today =
            DateOnly.FromDateTime(
                DateTime.Now);

        var tomorrow =
            today.AddDays(1);

        if (SelectedDayFilter == "Idag")
            return _fullPlan.ForDate(today);

        if (SelectedDayFilter == "Imorgon")
            return _fullPlan.ForDate(tomorrow);

        return _fullPlan;
    }

    /// <summary>
    /// Uppdaterar körblockstabellen och kvartstidsdetaljerna.
    /// </summary>
    private void UpdateTables(
        PumpPlan visiblePlan)
    {
        RunBlocks.Clear();
        DetailSlots.Clear();

        foreach (var block in visiblePlan.GetRunBlocks())
        {
            RunBlocks.Add(block);
        }

        foreach (var slot in
                 visiblePlan.Slots.OrderBy(
                     x => x.StartsAt))
        {
            DetailSlots.Add(slot);
        }
    }

    /// <summary>
    /// Uppdaterar sammanfattningskorten för idag och imorgon.
    /// </summary>
    private void UpdateSummary()
    {
        if (_fullPlan == null)
            return;

        var today =
            DateOnly.FromDateTime(
                DateTime.Now);

        var tomorrow =
            today.AddDays(1);

        var todayPlan =
            _fullPlan.ForDate(today);

        var tomorrowPlan =
            _fullPlan.ForDate(tomorrow);

        TodayRunText =
            $"{todayPlan.TotalRunHours:0.00} h";

        TomorrowRunText =
            $"{tomorrowPlan.TotalRunHours:0.00} h";

        TodayStartsText =
            todayPlan.Starts.ToString();

        AverageRunPriceText =
            $"{todayPlan.AverageRunPrice:0.000} SEK/kWh";
    }

    /// <summary>
    /// Behålls för kompatibilitet med den tidigare LiveCharts-modellen.
    /// Den egna PricePlanChart använder DetailSlots direkt.
    /// </summary>
    private void UpdateChart(
        PumpPlan visiblePlan)
    {
        PriceSeries =
            Array.Empty<ISeries>();

        OnPropertyChanged(nameof(PriceSeries));
        OnPropertyChanged(nameof(XAxes));
        OnPropertyChanged(nameof(YAxes));
    }

    /// <summary>
    /// Applicerar Shelly-statusen på dashboardens read-only-fält och
    /// uppdaterar den lokalt beräknade driftstatistiken.
    /// </summary>
    private void ApplyShellyStatus(
        ShellySwitchStatus status)
    {
        ShellyOnlineText = status.OnlineText;
        ShellyOutputText = status.OutputText;
        ShellyPowerText = status.ActivePowerText;
        ShellyVoltageText = status.VoltageText;
        ShellyCurrentText = status.CurrentText;
        ShellyLastReadTimeText = status.ReadAtTimeText;
        ShellyLastReadTooltipText =
            $"Senast läst: {status.ReadAtTooltipText}";

        if (!status.IsOnline || !status.IsOn.HasValue)
            return;

        var runtimeSnapshot = _pumpRuntimeTracker.Observe(
            status,
            GetMaximumReliableShellyObservationGap());

        ApplyPumpRuntimeSnapshot(runtimeSnapshot);
    }

    /// <summary>
    /// Uppdaterar dashboardens egna dagsräknare.
    /// </summary>
    private void ApplyPumpRuntimeSnapshot(
        PumpRuntimeSnapshot snapshot)
    {
        ShellyOnTimeText = snapshot.RunTimeTodayText;
        ShellySwitchOnCountText = snapshot.StartsTodayText;
        ShellyRuntimeTooltipText =
            $"Egen statistik för {snapshot.TrackingDate:yyyy-MM-dd}." +
            Environment.NewLine +
            $"Senaste start: {snapshot.LastStartedText}" +
            Environment.NewLine +
            $"Senaste stopp: {snapshot.LastStoppedText}" +
            Environment.NewLine +
            $"Senaste observation: {snapshot.LastObservedText}" +
            Environment.NewLine +
            "Endast verifierade intervall medan appen körs räknas.";
    }

    /// <summary>
    /// Uppdaterar visningen mellan Shelly-läsningarna utan att skriva
    /// runtime-state.json varje sekund.
    /// </summary>
    private void RefreshPumpRuntimeDisplay()
    {
        var snapshot = _pumpRuntimeTracker.GetLiveSnapshot(
            _config.ShellyDeviceId,
            _config.ShellySwitchId,
            DateTimeOffset.Now,
            GetMaximumReliableShellyObservationGap());

        ApplyPumpRuntimeSnapshot(snapshot);
    }

    /// <summary>
    /// Anger hur långt det maximalt får vara mellan två statusläsningar för
    /// att intervallet ska räknas som tillförlitlig drifttid. Appsessionen
    /// identifieras separat, så tid när appen varit stängd räknas aldrig.
    /// </summary>
    private static TimeSpan GetMaximumReliableShellyObservationGap()
    {
        return TimeSpan.FromHours(12);
    }

    /// <summary>
    /// Returnerar true när alla obligatoriska Shelly Cloud-inställningar finns.
    /// </summary>
    private bool HasShellyConfiguration()
    {
        return
            !string.IsNullOrWhiteSpace(ShellyCloudServer) &&
            !string.IsNullOrWhiteSpace(ShellyCloudAuthKey) &&
            !string.IsNullOrWhiteSpace(ShellyDeviceId);
    }

    /// <summary>
    /// Uppdaterar tid, tooltip och färg för senaste prisuppdatering.
    /// </summary>
    private void SetLastUpdated(
        DateTime value)
    {
        LastUpdatedTimeText =
            value.ToString("HH:mm:ss");

        LastUpdatedTooltipText =
            value.ToString("yyyy-MM-dd HH:mm:ss");

        LastUpdatedBrush =
            value.Date == DateTime.Today
                ? new SolidColorBrush(
                    Color.FromRgb(
                        242,
                        245,
                        248))
                : new SolidColorBrush(
                    Color.FromRgb(
                        255,
                        92,
                        122));
    }

    /// <summary>
    /// Uppdaterar statusfältet och lägger samma information i loggen.
    /// </summary>
    private void SetStatus(
        string message,
        string level)
    {
        StatusText = message;

        AddLog(
            level,
            message);
    }

    /// <summary>
    /// Lägger till en rad i statusloggen.
    /// </summary>
    private void AddLog(
        string level,
        string message)
    {
        LogEntries.Insert(
            0,
            new LogEntry(
                DateTime.Now,
                level,
                message));

        while (LogEntries.Count > 500)
        {
            LogEntries.RemoveAt(
                LogEntries.Count - 1);
        }
    }

    /// <summary>
    /// Begränsar ett decimalvärde till angivet intervall.
    /// </summary>
    private static decimal Clamp(
        decimal value,
        decimal minimum,
        decimal maximum)
    {
        if (value < minimum)
            return minimum;

        if (value > maximum)
            return maximum;

        return value;
    }
}