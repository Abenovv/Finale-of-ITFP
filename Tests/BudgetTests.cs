using Tracker.Application;
using Tracker.Domain;
using Tracker.Native;

namespace Tracker.Tests;

internal static partial class Program
{
    private static IEnumerable<(string Name, Action Run)> BudgetCases()
    {
        yield return ("Budget fit skips oversized tasks and preserves the original plan", () =>
        {
            var ranked = TaskPipeline.CreatePlan(Sample(), Day, 3, ScoringPolicies.ImportanceFirst);
            var result = BudgetFitting.Fit(ranked, 50);
            Assert(result is Result<BudgetFit>.Success, "A fitting plan was not found.");
            var fit = ((Result<BudgetFit>.Success)result).Value;
            Sequence(new[] { "T2", "T7" }, fit.Plan.Tasks.Select(item => item.Task.Id));
            Sequence(new[] { 1, 2 }, fit.Plan.Tasks.Select(item => item.Position));
            Sequence(new[] { "T1", "T3" }, fit.Deferred.Select(item => item.Task.Id));
            Equal(50, fit.Plan.TotalMinutes);
            Equal(120, ranked.TotalMinutes);
        });

        yield return ("Budget fit includes a zero-minute reminder and rejects an empty plan", () =>
        {
            var reminder = new TaskItem("R", "Reminder", Day, 3, 0, false);
            var ranked = TaskPipeline.CreatePlan([reminder], Day, 3, ScoringPolicies.ImportanceFirst);
            Assert(BudgetFitting.Fit(ranked, 1) is Result<BudgetFit>.Success { Value.Plan.TotalMinutes: 0 },
                "Zero-minute reminder was discarded.");
            Assert(BudgetFitting.Fit(new DailyPlan(Day, []), 1) is Result<BudgetFit>.Failure, "Empty plan accepted.");
            Assert(BudgetFitting.Fit(ranked, 0) is Result<BudgetFit>.Failure, "Zero budget accepted.");
        });

        yield return ("Budget workflow confirms a fitting plan and uses the actual native percentage", () =>
        {
            var result = BudgetWorkflow.Run("samples/tasks.json", Day, 3, 55, CurriedScoring.ImportanceFirst,
                new NativeLoadAdapter());
            Assert(result is Result<BudgetPreview>.Success, "Budget workflow failed.");
            var preview = ((Result<BudgetPreview>.Success)result).Value;
            Equal(50, preview.Confirmation.Session.Plan.TotalMinutes);
            Equal(5, preview.Confirmation.Session.RemainingMinutes);
            Equal(91, preview.LoadPercent);
        });

        yield return ("Budget workflow skips native calculation if no task fits and propagates a native failure", () =>
        {
            int calls = 0;
            var native = new NativeLoadAdapter((_, _) =>
            {
                calls++;
                throw new DllNotFoundException("Controlled budget test failure.");
            });

            var tooSmall = BudgetWorkflow.Run("samples/tasks.json", Day, 3, 1, CurriedScoring.ImportanceFirst, native);
            Assert(tooSmall is Result<BudgetPreview>.Failure { Error: "No eligible task fits the available time." },
                "Fit failure did not propagate.");
            Equal(0, calls);
            var unavailable = BudgetWorkflow.Run("samples/tasks.json", Day, 3, 55, CurriedScoring.ImportanceFirst, native);
            Assert(unavailable is Result<BudgetPreview>.Failure, "Native failure did not propagate.");
            Equal(1, calls);
        });
    }
}
