namespace Tracker.Domain;

public sealed record BudgetFit(DailyPlan Plan, IReadOnlyList<RankedTask> Deferred);

public static class BudgetFitting
{
    public static Result<BudgetFit> Fit(DailyPlan ranked, int capacity)
    {
        if (capacity is < 1 or > 480)
            return Result<BudgetFit>.Fail("Available time must be between 1 and 480 minutes.");

        var accepted = new List<RankedTask>();
        var deferred = new List<RankedTask>();
        int remaining = capacity;
        foreach (var item in ranked.Tasks)
        {
            if (item.Task.Minutes <= remaining)
            {
                accepted.Add(item with { Position = accepted.Count + 1 });
                remaining -= item.Task.Minutes;
            }
            else
            {
                deferred.Add(item);
            }
        }

        return accepted.Count == 0
            ? Result<BudgetFit>.Fail("No eligible task fits the available time.")
            : Result<BudgetFit>.Ok(new(new DailyPlan(ranked.Date, accepted), Array.AsReadOnly(deferred.ToArray())));
    }
}
