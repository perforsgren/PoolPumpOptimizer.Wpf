namespace PoolPumpOptimizer.Wpf.Models;

public sealed class PumpPlan
{
    public required List<PlannedSlot> Slots { get; init; }

    public double TotalRunHours =>
        Slots.Count(x => x.Run) * 0.25;

    public int Starts
    {
        get
        {
            var starts = 0;
            var wasRunning = false;

            foreach (var slot in Slots.OrderBy(x => x.StartsAt))
            {
                if (slot.Run && !wasRunning)
                    starts++;

                wasRunning = slot.Run;
            }

            return starts;
        }
    }

    public decimal AverageRunPrice =>
        Slots
            .Where(x => x.Run)
            .Select(x => x.PriceSekPerKwh)
            .DefaultIfEmpty(0m)
            .Average();

    public List<DateOnly> GetDays()
    {
        return Slots
            .Select(x => x.LocalDate)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
    }

    public List<RunBlock> GetRunBlocks()
    {
        var result = new List<RunBlock>();

        var ordered = Slots.OrderBy(x => x.StartsAt).ToList();

        List<PlannedSlot>? currentBlock = null;

        foreach (var slot in ordered)
        {
            if (slot.Run)
            {
                currentBlock ??= new List<PlannedSlot>();
                currentBlock.Add(slot);
            }
            else
            {
                FlushBlock(currentBlock, result);
                currentBlock = null;
            }
        }

        FlushBlock(currentBlock, result);

        return result;
    }

    public PumpPlan ForDate(DateOnly date)
    {
        return new PumpPlan
        {
            Slots = Slots
                .Where(x => x.LocalDate == date)
                .OrderBy(x => x.StartsAt)
                .ToList()
        };
    }

    private static void FlushBlock(
        List<PlannedSlot>? currentBlock,
        List<RunBlock> result)
    {
        if (currentBlock == null || currentBlock.Count == 0)
            return;

        var start = currentBlock.First().StartsAt;
        var end = currentBlock.Last().StartsAt.AddMinutes(15);
        var duration = end - start;
        var averagePrice = currentBlock.Average(x => x.PriceSekPerKwh);

        result.Add(new RunBlock(
            start,
            end,
            duration,
            averagePrice));
    }
}