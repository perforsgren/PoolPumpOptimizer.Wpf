using PoolPumpOptimizer.Wpf.Models;

namespace PoolPumpOptimizer.Wpf.Services;

public sealed class PoolPumpOptimizer
{
    private readonly PoolPumpConfig _config;

    public PoolPumpOptimizer(PoolPumpConfig config)
    {
        _config = config;
    }

    public PumpPlan BuildPlan(List<PriceSlot> prices)
    {
        var now = DateTimeOffset.Now;

        var filteredPrices = prices
            .Where(x => !_config.IgnorePastSlots || x.StartsAt >= RoundDownToCurrentQuarter(now))
            .OrderBy(x => x.StartsAt)
            .ToList();

        if (filteredPrices.Count == 0)
            throw new InvalidOperationException("Inga framtida priser att optimera på.");

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

    private PumpPlan BuildPlanForSingleDay(List<PriceSlot> prices)
    {
        if (prices.Count == 0)
            throw new InvalidOperationException("Inga priser för valt dygn.");

        var selected = new Dictionary<DateTimeOffset, string>();

        AddCheapestBlocks(prices, selected);
        AddExtraCheapSlots(prices, selected);

        var smoothed = ApplyStartStopProtection(prices, selected);

        return new PumpPlan { Slots = smoothed };
    }

    private void AddCheapestBlocks(
        List<PriceSlot> prices,
        Dictionary<DateTimeOffset, string> selected)
    {
        var blockSlots = _config.PreferredBlockHours * 4;
        var requiredSlots = _config.MinRunHoursPerDay * 4;

        if (blockSlots <= 0)
            throw new InvalidOperationException("Blocklängd måste vara större än 0.");

        if (requiredSlots <= 0)
            throw new InvalidOperationException("Min drift per dygn måste vara större än 0.");

        if (blockSlots > prices.Count)
        {
            foreach (var slot in prices)
                selected[slot.StartsAt] = "Tillgängliga kvarvarande tider";

            return;
        }

        var candidates = new List<BlockCandidate>();

        for (var i = 0; i <= prices.Count - blockSlots; i++)
        {
            var block = prices
                .Skip(i)
                .Take(blockSlots)
                .ToList();

            var avg = block.Average(x => x.PriceSekPerKwh);

            candidates.Add(new BlockCandidate(
                StartIndex: i,
                SlotCount: blockSlots,
                AveragePrice: avg));
        }

        foreach (var candidate in candidates.OrderBy(x => x.AveragePrice))
        {
            if (selected.Count >= requiredSlots)
                break;

            if (CountStarts(selected, prices) >= _config.MaxStartsPerDay)
                break;

            var block = prices
                .Skip(candidate.StartIndex)
                .Take(candidate.SlotCount)
                .ToList();

            if (block.Any(x => selected.ContainsKey(x.StartsAt)))
                continue;

            foreach (var slot in block)
                selected[slot.StartsAt] = $"Billigt {candidate.SlotCount / 4.0:0.##}h-block";
        }
    }

    private void AddExtraCheapSlots(
        List<PriceSlot> prices,
        Dictionary<DateTimeOffset, string> selected)
    {
        foreach (var slot in prices)
        {
            if (slot.PriceSekPerKwh < _config.CheapPriceThresholdSekPerKwh)
            {
                if (!selected.ContainsKey(slot.StartsAt))
                    selected[slot.StartsAt] = "Extra billigt pris";
            }
        }
    }

    private List<PlannedSlot> ApplyStartStopProtection(
        List<PriceSlot> prices,
        Dictionary<DateTimeOffset, string> selected)
    {
        var result = new List<PlannedSlot>();

        var minOnSlots = Math.Max(1, _config.MinOnMinutes / 15);
        var minOffSlots = Math.Max(1, _config.MinOffMinutes / 15);

        var isRunning = false;
        var slotsSinceSwitch = 999;

        foreach (var price in prices)
        {
            var wantsRun = selected.TryGetValue(price.StartsAt, out var reason);

            if (wantsRun && !isRunning && slotsSinceSwitch < minOffSlots)
            {
                wantsRun = false;
                reason = "Blockerad av MinOffTime";
            }

            if (!wantsRun && isRunning && slotsSinceSwitch < minOnSlots)
            {
                wantsRun = true;
                reason = "Förlängd av MinOnTime";
            }

            if (wantsRun != isRunning)
            {
                isRunning = wantsRun;
                slotsSinceSwitch = 0;
            }
            else
            {
                slotsSinceSwitch++;
            }

            result.Add(new PlannedSlot(
                price.StartsAt,
                price.PriceSekPerKwh,
                wantsRun,
                wantsRun ? reason ?? "Planerad drift" : ""));
        }

        return result;
    }

    private static int CountStarts(
        Dictionary<DateTimeOffset, string> selected,
        List<PriceSlot> prices)
    {
        var starts = 0;
        var wasRunning = false;

        foreach (var p in prices.OrderBy(x => x.StartsAt))
        {
            var running = selected.ContainsKey(p.StartsAt);

            if (running && !wasRunning)
                starts++;

            wasRunning = running;
        }

        return starts;
    }

    private static DateTimeOffset RoundDownToCurrentQuarter(DateTimeOffset value)
    {
        var minute = value.Minute - value.Minute % 15;

        return new DateTimeOffset(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            minute,
            0,
            value.Offset);
    }

    private sealed record BlockCandidate(
        int StartIndex,
        int SlotCount,
        decimal AveragePrice);
}