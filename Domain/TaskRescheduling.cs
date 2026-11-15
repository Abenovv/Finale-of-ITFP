namespace Tracker.Domain;

public static class TaskRescheduling
{
    public static Result<IReadOnlyList<TaskItem>> Move(IReadOnlyList<TaskItem> tasks, string id,
        DateOnly expectedDate, DateOnly newDate) => TaskLookup.Required(tasks, id).Bind(task =>
        task switch
        {
            { Completed: true } => Result<IReadOnlyList<TaskItem>>.Fail($"Task '{task.Id}' is already complete."),
            _ when task.Date != expectedDate => Result<IReadOnlyList<TaskItem>>.Fail(
                $"Task '{task.Id}' is scheduled for {task.Date:yyyy-MM-dd}, not {expectedDate:yyyy-MM-dd}."),
            _ when newDate <= task.Date => Result<IReadOnlyList<TaskItem>>.Fail(
                "The new date must be later than the scheduled date."),
            _ => Result<IReadOnlyList<TaskItem>>.Ok(Array.AsReadOnly(tasks.Select(item =>
                item.Id == task.Id ? item with { Date = newDate } : item).ToArray()))
        });
}
