using Tracker.Domain;
using Tracker.Input;

namespace Tracker;

internal static partial class Program
{
    private static Result<string> CompleteTask(string[] args)
    {
        if (args is not [var path, var id, var day] || !ParseDate(day, out var date))
            return Result<string>.Fail("Usage: DailyPlanner --complete FILE ID COMPLETION_DATE");

        return TaskUpdates.Apply(path, tasks => TaskCompletion.Complete(tasks, id, date))
            .Map(_ => $"Completed '{id.Trim()}' on {date:yyyy-MM-dd}.");
    }
}
