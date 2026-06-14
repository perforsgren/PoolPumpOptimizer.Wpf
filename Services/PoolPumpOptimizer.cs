using PoolPumpOptimizer.Wpf.Models;

namespace PoolPumpOptimizer.Wpf.Services;

public sealed class PoolPumpOptimizer
{
    private const int SlotMinutes = 15;

    private readonly PoolPumpConfig _config;

    public PoolPumpOptimizer(PoolPumpConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        ValidateConfig();
    }

    /// <summary>
    /// Bygger en pumpplan för tillgängliga Tibber-priser.
    /// Optimeringen görs separat för varje lokalt kalenderdygn.
    /// </summary>
    public PumpPlan BuildPlan(List<PriceSlot> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        var now = DateTimeOffset.Now;

        var filteredPrices = prices
            .Where(x =>
                !_config.IgnorePastSlots ||
                x.StartsAt >= RoundDownToCurrentQuarter(now))
            .OrderBy(x => x.StartsAt)
            .ToList();

        if (filteredPrices.Count == 0)
        {
            throw new InvalidOperationException(
                "Inga framtida priser att optimera på.");
        }

        var allPlannedSlots = new List<PlannedSlot>();

        var pricesByDay = filteredPrices
            .GroupBy(x => x.LocalDate)
            .OrderBy(x => x.Key);

        foreach (var dayGroup in pricesByDay)
        {
            var dayPrices = dayGroup
                .OrderBy(x => x.StartsAt)
                .ToList();

            var dayPlan = BuildPlanForSingleDay(dayPrices);

            allPlannedSlots.AddRange(dayPlan.Slots);
        }

        return new PumpPlan
        {
            Slots = allPlannedSlots
                .OrderBy(x => x.StartsAt)
                .ToList()
        };
    }

    /// <summary>
    /// Bygger planen för ett enskilt kalenderdygn.
    /// </summary>
    private PumpPlan BuildPlanForSingleDay(List<PriceSlot> prices)
    {
        if (prices.Count == 0)
        {
            throw new InvalidOperationException(
                "Inga priser finns för valt dygn.");
        }

        var selected = new Dictionary<DateTimeOffset, string>();

        AddCheapestBlocks(prices, selected);
        AddExtraCheapPeriods(prices, selected);

        var protectedSlots = ApplyStartStopProtection(
            prices,
            selected);

        return new PumpPlan
        {
            Slots = protectedSlots
        };
    }

    /// <summary>
    /// Väljer billiga sammanhängande block tills minsta dagliga
    /// driftbehov är uppfyllt eller max antal starter har nåtts.
    /// </summary>
    private void AddCheapestBlocks(
        List<PriceSlot> prices,
        Dictionary<DateTimeOffset, string> selected)
    {
        var blockSlots = HoursToSlots(_config.PreferredBlockHours);
        var requiredSlots = HoursToSlots(_config.MinRunHoursPerDay);

        if (blockSlots > prices.Count)
        {
            foreach (var slot in prices)
            {
                selected[slot.StartsAt] =
                    "Tillgänglig återstående tid";
            }

            return;
        }

        var candidates = new List<BlockCandidate>();

        for (var startIndex = 0;
             startIndex <= prices.Count - blockSlots;
             startIndex++)
        {
            var block = prices
                .Skip(startIndex)
                .Take(blockSlots)
                .ToList();

            if (!IsContinuous(block))
                continue;

            var averagePrice = block.Average(
                x => x.PriceSekPerKwh);

            candidates.Add(
                new BlockCandidate(
                    startIndex,
                    blockSlots,
                    averagePrice));
        }

        foreach (var candidate in
                 candidates.OrderBy(x => x.AveragePrice))
        {
            if (selected.Count >= requiredSlots)
                break;

            var block = prices
                .Skip(candidate.StartIndex)
                .Take(candidate.SlotCount)
                .ToList();

            if (block.Any(
                    x => selected.ContainsKey(x.StartsAt)))
            {
                continue;
            }

            if (!CanAddSlotsWithoutExceedingStartLimit(
                    prices,
                    selected,
                    block))
            {
                continue;
            }

            foreach (var slot in block)
            {
                selected[slot.StartsAt] =
                    $"Billigt {candidate.SlotCount / 4.0:0.##} h-block";
            }
        }
    }

    /// <summary>
    /// Hanterar perioder under gränsen för extra billigt pris.
    ///
    /// En kort billigperiod får förlänga ett redan valt block.
    /// För att skapa ett nytt fristående block måste billigperioden
    /// vara minst både MinExtraCheapBlockMinutes och MinOnMinutes.
    /// </summary>
    private void AddExtraCheapPeriods(
        List<PriceSlot> prices,
        Dictionary<DateTimeOffset, string> selected)
    {
        var cheapPeriods = FindExtraCheapPeriods(prices);

        var minimumStandaloneMinutes = Math.Max(
            RoundUpToQuarter(_config.MinExtraCheapBlockMinutes),
            RoundUpToQuarter(_config.MinOnMinutes));

        var minimumStandaloneSlots =
            MinutesToSlots(minimumStandaloneMinutes);

        foreach (var period in cheapPeriods)
        {
            var touchesExistingBlock = PeriodTouchesSelectedBlock(
                prices,
                selected,
                period);

            if (touchesExistingBlock)
            {
                AddCheapPeriod(
                    period,
                    selected,
                    "Förlängd av extra billigt pris");

                continue;
            }

            if (period.Count < minimumStandaloneSlots)
            {
                // En ensam eller för kort billig period får inte
                // starta pumpen och därmed trigga MinOnTime.
                continue;
            }

            if (!CanAddSlotsWithoutExceedingStartLimit(
                    prices,
                    selected,
                    period))
            {
                continue;
            }

            AddCheapPeriod(
                period,
                selected,
                $"Extra billig sammanhängande period, " +
                $"{period.Count * SlotMinutes} min");
        }
    }

    /// <summary>
    /// Identifierar sammanhängande perioder där priset ligger
    /// under gränsen för extra billigt pris.
    /// </summary>
    private List<List<PriceSlot>> FindExtraCheapPeriods(
        List<PriceSlot> prices)
    {
        var result = new List<List<PriceSlot>>();
        List<PriceSlot>? currentPeriod = null;

        foreach (var slot in prices.OrderBy(x => x.StartsAt))
        {
            var isCheap =
                slot.PriceSekPerKwh <
                _config.CheapPriceThresholdSekPerKwh;

            if (!isCheap)
            {
                FlushPeriod(currentPeriod, result);
                currentPeriod = null;
                continue;
            }

            if (currentPeriod == null)
            {
                currentPeriod = new List<PriceSlot>
                {
                    slot
                };

                continue;
            }

            var expectedStart =
                currentPeriod[^1].StartsAt.AddMinutes(SlotMinutes);

            if (slot.StartsAt == expectedStart)
            {
                currentPeriod.Add(slot);
            }
            else
            {
                FlushPeriod(currentPeriod, result);

                currentPeriod = new List<PriceSlot>
                {
                    slot
                };
            }
        }

        FlushPeriod(currentPeriod, result);

        return result;
    }

    /// <summary>
    /// Avgör om billigperioden direkt ansluter till ett redan
    /// planerat ON-block.
    /// </summary>
    private static bool PeriodTouchesSelectedBlock(
        List<PriceSlot> allPrices,
        Dictionary<DateTimeOffset, string> selected,
        List<PriceSlot> period)
    {
        if (period.Count == 0 || selected.Count == 0)
            return false;

        var orderedPrices = allPrices
            .OrderBy(x => x.StartsAt)
            .ToList();

        var firstStart = period[0].StartsAt;
        var lastStart = period[^1].StartsAt;

        var firstIndex = orderedPrices.FindIndex(
            x => x.StartsAt == firstStart);

        var lastIndex = orderedPrices.FindIndex(
            x => x.StartsAt == lastStart);

        var touchesBefore =
            firstIndex > 0 &&
            selected.ContainsKey(
                orderedPrices[firstIndex - 1].StartsAt);

        var touchesAfter =
            lastIndex >= 0 &&
            lastIndex < orderedPrices.Count - 1 &&
            selected.ContainsKey(
                orderedPrices[lastIndex + 1].StartsAt);

        return touchesBefore || touchesAfter;
    }

    /// <summary>
    /// Lägger till en billigperiod i det valda schemat utan att
    /// skriva över orsaken för redan valda ordinarie block.
    /// </summary>
    private static void AddCheapPeriod(
        IEnumerable<PriceSlot> period,
        Dictionary<DateTimeOffset, string> selected,
        string reason)
    {
        foreach (var slot in period)
        {
            if (!selected.ContainsKey(slot.StartsAt))
            {
                selected[slot.StartsAt] = reason;
            }
        }
    }

    /// <summary>
    /// Simulerar att kandidatslots läggs till och säkerställer att
    /// max antal starter inte överskrids.
    /// </summary>
    private bool CanAddSlotsWithoutExceedingStartLimit(
        List<PriceSlot> prices,
        Dictionary<DateTimeOffset, string> selected,
        IEnumerable<PriceSlot> candidateSlots)
    {
        var simulatedSelection =
            new HashSet<DateTimeOffset>(selected.Keys);

        foreach (var slot in candidateSlots)
        {
            simulatedSelection.Add(slot.StartsAt);
        }

        var starts = CountStarts(
            simulatedSelection,
            prices);

        return starts <= _config.MaxStartsPerDay;
    }

    /// <summary>
    /// Tillämpar minsta ON- och OFF-tid.
    ///
    /// Räknaren inkluderar den första kvarten i aktuellt state,
    /// vilket gör att exempelvis 90 minuter motsvarar exakt sex
    /// kvartar och inte sju.
    /// </summary>
    private List<PlannedSlot> ApplyStartStopProtection(
        List<PriceSlot> prices,
        Dictionary<DateTimeOffset, string> selected)
    {
        var result = new List<PlannedSlot>();

        var minOnSlots = MinutesToSlots(
            RoundUpToQuarter(_config.MinOnMinutes));

        var minOffSlots = MinutesToSlots(
            RoundUpToQuarter(_config.MinOffMinutes));

        var isRunning = false;

        // Ett stort initialvärde innebär att pumpen får starta
        // direkt vid planens början.
        var slotsInCurrentState = int.MaxValue;

        foreach (var price in prices.OrderBy(x => x.StartsAt))
        {
            var requestedRun = selected.TryGetValue(
                price.StartsAt,
                out var selectedReason);

            var actualRun = requestedRun;
            var reason = selectedReason ?? "";

            if (requestedRun != isRunning)
            {
                if (isRunning &&
                    slotsInCurrentState < minOnSlots)
                {
                    actualRun = true;
                    reason = "Förlängd av minsta ON-tid";
                }
                else if (!isRunning &&
                         slotsInCurrentState < minOffSlots)
                {
                    actualRun = false;
                    reason = "";
                }
            }

            if (actualRun != isRunning)
            {
                isRunning = actualRun;

                // Den aktuella kvarten är den första kvarten i
                // det nya tillståndet.
                slotsInCurrentState = 1;
            }
            else if (slotsInCurrentState < int.MaxValue)
            {
                slotsInCurrentState++;
            }

            result.Add(
                new PlannedSlot(
                    price.StartsAt,
                    price.PriceSekPerKwh,
                    actualRun,
                    actualRun
                        ? string.IsNullOrWhiteSpace(reason)
                            ? "Planerad drift"
                            : reason
                        : ""));
        }

        return result;
    }

    /// <summary>
    /// Räknar antalet OFF-till-ON-övergångar i en vald slotmängd.
    /// </summary>
    private static int CountStarts(
        HashSet<DateTimeOffset> selected,
        List<PriceSlot> prices)
    {
        var starts = 0;
        var wasRunning = false;

        foreach (var price in prices.OrderBy(x => x.StartsAt))
        {
            var isRunning =
                selected.Contains(price.StartsAt);

            if (isRunning && !wasRunning)
            {
                starts++;
            }

            wasRunning = isRunning;
        }

        return starts;
    }

    /// <summary>
    /// Kontrollerar att alla slots i ett block ligger med exakt
    /// 15 minuters mellanrum.
    /// </summary>
    private static bool IsContinuous(
        IReadOnlyList<PriceSlot> slots)
    {
        for (var index = 1;
             index < slots.Count;
             index++)
        {
            var expectedStart =
                slots[index - 1]
                    .StartsAt
                    .AddMinutes(SlotMinutes);

            if (slots[index].StartsAt != expectedStart)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Flyttar tiden nedåt till aktuell kvart.
    /// </summary>
    private static DateTimeOffset RoundDownToCurrentQuarter(
        DateTimeOffset value)
    {
        var minute =
            value.Minute -
            value.Minute % SlotMinutes;

        return new DateTimeOffset(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            minute,
            0,
            value.Offset);
    }

    /// <summary>
    /// Rundar minuter uppåt till närmaste kvart.
    /// </summary>
    private static int RoundUpToQuarter(int minutes)
    {
        if (minutes <= 0)
            return SlotMinutes;

        return
            (int)Math.Ceiling(
                minutes / (double)SlotMinutes) *
            SlotMinutes;
    }

    /// <summary>
    /// Konverterar hela timmar till antal kvartsslots.
    /// </summary>
    private static int HoursToSlots(int hours)
    {
        return checked(hours * 4);
    }

    /// <summary>
    /// Konverterar ett kvartjusterat minutvärde till antal slots.
    /// </summary>
    private static int MinutesToSlots(int minutes)
    {
        return Math.Max(
            1,
            minutes / SlotMinutes);
    }

    /// <summary>
    /// Flyttar färdig billigperiod till resultatlistan.
    /// </summary>
    private static void FlushPeriod(
        List<PriceSlot>? currentPeriod,
        List<List<PriceSlot>> result)
    {
        if (currentPeriod == null ||
            currentPeriod.Count == 0)
        {
            return;
        }

        result.Add(currentPeriod);
    }

    /// <summary>
    /// Validerar inställningarna innan optimeringen körs.
    /// </summary>
    private void ValidateConfig()
    {
        if (_config.MinRunHoursPerDay is < 1 or > 24)
        {
            throw new InvalidOperationException(
                "Min drift per dygn måste vara mellan 1 och 24 timmar.");
        }

        if (_config.PreferredBlockHours is < 1 or > 24)
        {
            throw new InvalidOperationException(
                "Blocklängden måste vara mellan 1 och 24 timmar.");
        }

        if (_config.MaxStartsPerDay is < 1 or > 24)
        {
            throw new InvalidOperationException(
                "Max starter per dygn måste vara mellan 1 och 24.");
        }

        if (_config.MinExtraCheapBlockMinutes is < 15 or > 1440)
        {
            throw new InvalidOperationException(
                "Min extra billig period måste vara mellan " +
                "15 och 1440 minuter.");
        }

        if (_config.MinOnMinutes is < 15 or > 1440)
        {
            throw new InvalidOperationException(
                "Min ON-tid måste vara mellan 15 och 1440 minuter.");
        }

        if (_config.MinOffMinutes is < 15 or > 1440)
        {
            throw new InvalidOperationException(
                "Min OFF-tid måste vara mellan 15 och 1440 minuter.");
        }
    }

    private sealed record BlockCandidate(
        int StartIndex,
        int SlotCount,
        decimal AveragePrice);
}