using System.Collections.ObjectModel;
using System.Windows.Media;
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
    private Brush _lastUpdatedBrush = new SolidColorBrush(Color.FromRgb(242, 245, 248));

    private DateTime? _lastUpdated;
    private TibberHome? _selectedHome;
    private string _selectedDayFilter = "Alla";

    private decimal _minRunHoursPerDay;
    private decimal _preferredBlockHours;
    private decimal _maxStartsPerDay;
    private decimal _cheapPriceThresholdSekPerKwh;
    private decimal _minOnMinutes;
    private decimal _minOffMinutes;

    private bool _isInitialized;
    private bool _isPlanTabSelected = true;
    private bool _isDetailsTabSelected;
    private bool _isLogTabSelected;

    public MainViewModel()
    {
        _config = _settingsService.Load();

        TibberToken = _config.TibberToken;
        UseProxy = _config.UseProxy;
        ProxyAddress = _config.ProxyAddress ?? "";

        _minRunHoursPerDay = _config.MinRunHoursPerDay;
        _preferredBlockHours = _config.PreferredBlockHours;
        _maxStartsPerDay = _config.MaxStartsPerDay;
        _cheapPriceThresholdSekPerKwh = _config.CheapPriceThresholdSekPerKwh;
        _minOnMinutes = _config.MinOnMinutes;
        _minOffMinutes = _config.MinOffMinutes;

        IgnorePastSlots = _config.IgnorePastSlots;

        DayFilters.Add("Alla");
        DayFilters.Add("Idag");
        DayFilters.Add("Imorgon");

        LoadHomesCommand = new RelayCommand(LoadHomesAsync);
        FetchPricesCommand = new RelayCommand(FetchPricesAsync);
        OptimizeCommand = new RelayCommand(OptimizeAsync);
        SaveSettingsCommand = new RelayCommand(SaveSettingsAsync);
        LoadSettingsCommand = new RelayCommand(LoadSettingsAsync);
        ClearLogCommand = new RelayCommand(ClearLogAsync);

        PriceSeries = Array.Empty<ISeries>();

        XAxes =
        [
            new Axis
            {
                LabelsRotation = 45,
                TextSize = 11,
                LabelsPaint = new SolidColorPaint(new SKColor(205, 213, 225)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(52, 58, 70))
            }
        ];

        YAxes =
        [
            new Axis
            {
                Name = "SEK/kWh",
                TextSize = 11,
                LabelsPaint = new SolidColorPaint(new SKColor(205, 213, 225)),
                SeparatorsPaint = new SolidColorPaint(new SKColor(52, 58, 70))
            }
        ];

        AddLog("Info", "Applikationen startades.");
        AddLog("Info", $"Inställningsfil: {_settingsService.SettingsPath}");
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

    public ISeries[] PriceSeries { get; private set; }

    public Axis[] XAxes { get; private set; }

    public Axis[] YAxes { get; private set; }

    public bool IsPlanTabSelected
    {
        get => _isPlanTabSelected;
        set
        {
            if (!SetProperty(ref _isPlanTabSelected, value))
                return;

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
            if (!SetProperty(ref _isDetailsTabSelected, value))
                return;

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
            if (!SetProperty(ref _isLogTabSelected, value))
                return;

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

    public bool IsPlanVisible => IsPlanTabSelected;

    public bool IsDetailsVisible => IsDetailsTabSelected;

    public bool IsLogVisible => IsLogTabSelected;

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

    public decimal MinRunHoursPerDay
    {
        get => _minRunHoursPerDay;
        set
        {
            var clamped = Clamp(value, 1m, 24m);

            if (!SetProperty(ref _minRunHoursPerDay, clamped))
                return;

            ReoptimizeIfPossible();
        }
    }

    public decimal PreferredBlockHours
    {
        get => _preferredBlockHours;
        set
        {
            var clamped = Clamp(value, 1m, 24m);

            if (!SetProperty(ref _preferredBlockHours, clamped))
                return;

            ReoptimizeIfPossible();
        }
    }

    public decimal MaxStartsPerDay
    {
        get => _maxStartsPerDay;
        set
        {
            var clamped = Clamp(value, 1m, 24m);

            if (!SetProperty(ref _maxStartsPerDay, clamped))
                return;

            ReoptimizeIfPossible();
        }
    }

    public decimal CheapPriceThresholdSekPerKwh
    {
        get => _cheapPriceThresholdSekPerKwh;
        set
        {
            var clamped = Clamp(value, -10m, 20m);

            if (!SetProperty(ref _cheapPriceThresholdSekPerKwh, clamped))
                return;

            ReoptimizeIfPossible();
        }
    }

    public decimal MinOnMinutes
    {
        get => _minOnMinutes;
        set
        {
            var clamped = Clamp(value, 15m, 1440m);

            if (!SetProperty(ref _minOnMinutes, clamped))
                return;

            ReoptimizeIfPossible();
        }
    }

    public decimal MinOffMinutes
    {
        get => _minOffMinutes;
        set
        {
            var clamped = Clamp(value, 15m, 1440m);

            if (!SetProperty(ref _minOffMinutes, clamped))
                return;

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
            if (!SetProperty(ref _selectedHome, value))
                return;

            _config.PreferredTibberHomeId = value?.Id;
            SelectedHomeText = value?.NicknameText ?? "-";

            if (_isInitialized && value != null)
            {
                SaveSelectedHomeQuietly();
                AddLog("Info", $"Valt home: {value.NicknameText}");
            }
        }
    }

    public string SelectedDayFilter
    {
        get => _selectedDayFilter;
        set
        {
            if (!SetProperty(ref _selectedDayFilter, value))
                return;

            RefreshVisiblePlan();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SelectedHomeText
    {
        get => _selectedHomeText;
        private set => SetProperty(ref _selectedHomeText, value);
    }

    public string TodayRunText
    {
        get => _todayRunText;
        private set => SetProperty(ref _todayRunText, value);
    }

    public string TomorrowRunText
    {
        get => _tomorrowRunText;
        private set => SetProperty(ref _tomorrowRunText, value);
    }

    public string TodayStartsText
    {
        get => _todayStartsText;
        private set => SetProperty(ref _todayStartsText, value);
    }

    public string AverageRunPriceText
    {
        get => _averageRunPriceText;
        private set => SetProperty(ref _averageRunPriceText, value);
    }

    public string LastUpdatedTimeText
    {
        get => _lastUpdatedTimeText;
        private set => SetProperty(ref _lastUpdatedTimeText, value);
    }

    public string LastUpdatedTooltipText
    {
        get => _lastUpdatedTooltipText;
        private set => SetProperty(ref _lastUpdatedTooltipText, value);
    }

    public Brush LastUpdatedBrush
    {
        get => _lastUpdatedBrush;
        private set => SetProperty(ref _lastUpdatedBrush, value);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        _isInitialized = true;

        if (string.IsNullOrWhiteSpace(TibberToken))
        {
            SetStatus("Redo. Ange Tibber-token i inställningar.", "Info");
            return;
        }

        SetStatus("Token hittad. Laddar Tibber homes automatiskt...", "Info");

        await LoadHomesAsync();
    }

    private async Task LoadHomesAsync()
    {
        try
        {
            SetStatus("Laddar Tibber homes...", "Info");

            UpdateConfigFromUi();

            var client = new TibberClient(_config);
            var homes = await client.GetHomesAsync();

            Homes.Clear();

            foreach (var home in homes)
                Homes.Add(home);

            SelectedHome =
                Homes.FirstOrDefault(x => x.Id == _config.PreferredTibberHomeId)
                ?? Homes.FirstOrDefault(x => x.HasCurrentSubscription)
                ?? Homes.FirstOrDefault();

            SetStatus($"Laddade {Homes.Count} Tibber home(s).", "OK");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "Fel");
        }
    }

    private async Task FetchPricesAsync()
    {
        try
        {
            SetStatus("Hämtar Tibber-priser...", "Info");

            UpdateConfigFromUi();

            var client = new TibberClient(_config);
            _prices = await client.GetQuarterPricesAsync();

            SetLastUpdated(DateTime.Now);

            SetStatus($"Hämtade {_prices.Count} prisrader.", "OK");

            await OptimizeAsync();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "Fel");
        }
    }

    private Task OptimizeAsync()
    {
        try
        {
            if (_prices.Count == 0)
            {
                SetStatus("Inga priser laddade. Hämta priser först.", "Varning");
                return Task.CompletedTask;
            }

            UpdateConfigFromUi();

            var optimizer = new Services.PoolPumpOptimizer(_config);
            _fullPlan = optimizer.BuildPlan(_prices);

            RefreshVisiblePlan();
            UpdateSummary();

            SetStatus("Optimering klar.", "OK");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "Fel");
        }

        return Task.CompletedTask;
    }

    private Task SaveSettingsAsync()
    {
        try
        {
            UpdateConfigFromUi();

            _settingsService.Save(_config);

            SetStatus($"Inställningar sparade: {_settingsService.SettingsPath}", "OK");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "Fel");
        }

        return Task.CompletedTask;
    }

    private Task LoadSettingsAsync()
    {
        try
        {
            _config = _settingsService.Load();

            TibberToken = _config.TibberToken;
            UseProxy = _config.UseProxy;
            ProxyAddress = _config.ProxyAddress ?? "";

            MinRunHoursPerDay = _config.MinRunHoursPerDay;
            PreferredBlockHours = _config.PreferredBlockHours;
            MaxStartsPerDay = _config.MaxStartsPerDay;
            CheapPriceThresholdSekPerKwh = _config.CheapPriceThresholdSekPerKwh;
            MinOnMinutes = _config.MinOnMinutes;
            MinOffMinutes = _config.MinOffMinutes;
            IgnorePastSlots = _config.IgnorePastSlots;

            SelectedHome =
                Homes.FirstOrDefault(x => x.Id == _config.PreferredTibberHomeId)
                ?? SelectedHome;

            SetStatus("Inställningar laddade.", "OK");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "Fel");
        }

        return Task.CompletedTask;
    }

    private Task ClearLogAsync()
    {
        LogEntries.Clear();
        AddLog("Info", "Loggen rensades.");

        return Task.CompletedTask;
    }

    private void SaveSelectedHomeQuietly()
    {
        try
        {
            UpdateConfigFromUi();
            _settingsService.Save(_config);
        }
        catch (Exception ex)
        {
            AddLog("Fel", $"Kunde inte spara valt home: {ex.Message}");
        }
    }

    private void UpdateConfigFromUi()
    {
        _config.TibberToken = TibberToken;
        _config.UseProxy = UseProxy;
        _config.ProxyAddress = ProxyAddress;

        _config.MinRunHoursPerDay = (int)Math.Round(MinRunHoursPerDay);
        _config.PreferredBlockHours = (int)Math.Round(PreferredBlockHours);
        _config.MaxStartsPerDay = (int)Math.Round(MaxStartsPerDay);

        _config.CheapPriceThresholdSekPerKwh = CheapPriceThresholdSekPerKwh;

        _config.MinOnMinutes = (int)Math.Round(MinOnMinutes);
        _config.MinOffMinutes = (int)Math.Round(MinOffMinutes);

        _config.IgnorePastSlots = IgnorePastSlots;
        _config.PreferredTibberHomeId = SelectedHome?.Id ?? _config.PreferredTibberHomeId;

        ValidateConfig();
    }

    private void ValidateConfig()
    {
        _config.MinRunHoursPerDay = Math.Clamp(_config.MinRunHoursPerDay, 1, 24);
        _config.PreferredBlockHours = Math.Clamp(_config.PreferredBlockHours, 1, 24);
        _config.MaxStartsPerDay = Math.Clamp(_config.MaxStartsPerDay, 1, 24);
        _config.MinOnMinutes = Math.Clamp(_config.MinOnMinutes, 15, 1440);
        _config.MinOffMinutes = Math.Clamp(_config.MinOffMinutes, 15, 1440);
        _config.CheapPriceThresholdSekPerKwh = Math.Clamp(_config.CheapPriceThresholdSekPerKwh, -10m, 20m);
    }

    private void ReoptimizeIfPossible()
    {
        if (_prices.Count == 0)
            return;

        try
        {
            UpdateConfigFromUi();

            var optimizer = new Services.PoolPumpOptimizer(_config);
            _fullPlan = optimizer.BuildPlan(_prices);

            RefreshVisiblePlan();
            UpdateSummary();

            StatusText = "Optimerade automatiskt.";
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "Fel");
        }
    }

    private void RefreshVisiblePlan()
    {
        if (_fullPlan == null)
            return;

        var visiblePlan = GetVisiblePlan();

        UpdateTables(visiblePlan);
        UpdateChart(visiblePlan);
    }

    private PumpPlan GetVisiblePlan()
    {
        if (_fullPlan == null)
        {
            return new PumpPlan
            {
                Slots = new List<PlannedSlot>()
            };
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var tomorrow = today.AddDays(1);

        if (SelectedDayFilter == "Idag")
            return _fullPlan.ForDate(today);

        if (SelectedDayFilter == "Imorgon")
            return _fullPlan.ForDate(tomorrow);

        return _fullPlan;
    }

    private void UpdateTables(PumpPlan visiblePlan)
    {
        RunBlocks.Clear();
        DetailSlots.Clear();

        foreach (var block in visiblePlan.GetRunBlocks())
            RunBlocks.Add(block);

        foreach (var slot in visiblePlan.Slots.OrderBy(x => x.StartsAt))
            DetailSlots.Add(slot);
    }

    private void UpdateSummary()
    {
        if (_fullPlan == null)
            return;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var tomorrow = today.AddDays(1);

        var todayPlan = _fullPlan.ForDate(today);
        var tomorrowPlan = _fullPlan.ForDate(tomorrow);

        TodayRunText = $"{todayPlan.TotalRunHours:0.00} h";
        TomorrowRunText = $"{tomorrowPlan.TotalRunHours:0.00} h";
        TodayStartsText = todayPlan.Starts.ToString();
        AverageRunPriceText = $"{todayPlan.AverageRunPrice:0.000} SEK/kWh";
    }

    private void UpdateChart(PumpPlan visiblePlan)
    {
        PriceSeries = Array.Empty<ISeries>();

        OnPropertyChanged(nameof(PriceSeries));
        OnPropertyChanged(nameof(XAxes));
        OnPropertyChanged(nameof(YAxes));
    }

    private void SetLastUpdated(DateTime value)
    {
        _lastUpdated = value;

        LastUpdatedTimeText = value.ToString("HH:mm:ss");
        LastUpdatedTooltipText = $"Senast uppdaterad: {value:yyyy-MM-dd HH:mm:ss}";

        LastUpdatedBrush = value.Date == DateTime.Today
            ? new SolidColorBrush(Color.FromRgb(242, 245, 248))
            : new SolidColorBrush(Color.FromRgb(255, 92, 122));
    }

    private void SetStatus(string message, string level)
    {
        StatusText = message;
        AddLog(level, message);
    }

    private void AddLog(string level, string message)
    {
        LogEntries.Insert(
            0,
            new LogEntry(
                DateTime.Now,
                level,
                message));

        while (LogEntries.Count > 500)
            LogEntries.RemoveAt(LogEntries.Count - 1);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        if (value < min)
            return min;

        if (value > max)
            return max;

        return value;
    }
}