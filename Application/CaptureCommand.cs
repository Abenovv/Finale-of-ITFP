using Tracker.Domain;
using Tracker.Input;

namespace Tracker;

internal static partial class Program
{
    private static Result<string> AddTask(string[] args)
    {
        if (args is not [var path, var id, var title, var day, var importance, var minutes]
            || !ParseDate(day, out var date)
            || !int.TryParse(importance, out int priority)
            || !int.TryParse(minutes, out int duration))
            return Result<string>.Fail("Usage: DailyPlanner --add FILE ID TITLE yyyy-MM-dd IMPORTANCE MINUTES");

        return TaskDraft.Create(id, title, date, priority, duration)
            .Bind(draft => TaskUpdates.Apply(path, draft.AddTo))
            .Map(tasks => $"Added '{id.Trim()}' on {date:yyyy-MM-dd}. Task file now contains {tasks.Count} tasks.");
    }
}
