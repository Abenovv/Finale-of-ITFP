using Tracker.Domain;
using Tracker.Input;
using Tracker.Native;

namespace Tracker.Application;

public sealed record BudgetPreview(PlanningState.Confirmed Confirmation, IReadOnlyList<RankedTask> Deferred, int LoadPercent);

public static class BudgetWorkflow
{
    public static Result<BudgetPreview> Run(string path, DateOnly date, int minimum, int available,
        Func<TaskItem, int> score, NativeLoadAdapter native)
    {
        if (minimum is < 1 or > 5)
            return Result<BudgetPreview>.Fail("Minimum importance must be from 1 to 5.");
        if (available is < 1 or > 480)
            return Result<BudgetPreview>.Fail("Available time must be between 1 and 480 minutes.");

        return TaskFile.Load(path)
            .Map(tasks => TaskPipeline.CreatePlan(tasks, date, minimum, score))
            .Bind(plan => BudgetFitting.Fit(plan, available))
            .Bind(fit => TaskRequestWorkflow.PrepareAndConfirm(PlanningState.Start(fit.Plan, available))
                .Bind(confirmed => native.Calculate(confirmed.Session.Plan.TotalMinutes, available)
                    .Map(load => new BudgetPreview(confirmed, fit.Deferred, load))));
    }
}
