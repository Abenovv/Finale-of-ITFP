using Tracker.Application;
using Tracker.Domain;
using Tracker.Native;

namespace Tracker;

internal static partial class Program
{
    private static Result<string> FitBudget(string[] args)
    {
        if (args is not [var path, var day, var policy, var minimum, var minutes]
            || !ParseDate(day, out var date)
            || !int.TryParse(minimum, out int threshold)
            || !int.TryParse(minutes, out int available))
            return Result<string>.Fail("Usage: DailyPlanner --fit FILE yyyy-MM-dd importance|short MINIMUM MINUTES");

        return Policies(policy).Bind(policies => policies.Count == 1
            ? BudgetWorkflow.Run(path, date, threshold, available, policies[0].Score, new NativeLoadAdapter())
                .Map(preview =>
                {
                    var deferred = preview.Deferred.Select(item => item.Task.Id).ToArray();
                    string summary = deferred.Length == 0 ? "none" : string.Join(", ", deferred);
                    return DescribePlan(policies[0].Name, preview.Confirmation.Session.Plan, preview.Confirmation)
                        + $"\nDeferred: {summary}\nBudget used: {preview.LoadPercent}% (native C)";
                })
            : Result<string>.Fail("Budget fitting needs one policy: importance or short."));
    }
}
