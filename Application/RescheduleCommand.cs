using Tracker.Domain;
using Tracker.Input;

namespace Tracker;

internal static partial class Program
{
    private static Result<string> RescheduleTask(string[] args)
    {
        if (args is not [var path, var id, var original, var target]
            || !ParseDate(original, out var expectedDate)
            || !ParseDate(target, out var newDate))
            return Result<string>.Fail("Usage: DailyPlanner --reschedule FILE ID CURRENT_DATE NEW_DATE");

        return TaskUpdates.Apply(path, tasks => TaskRescheduling.Move(tasks, id, expectedDate, newDate))
            .Map(_ => $"Rescheduled '{id.Trim()}' from {expectedDate:yyyy-MM-dd} to {newDate:yyyy-MM-dd}.");
    }
}
