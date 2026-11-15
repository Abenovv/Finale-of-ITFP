namespace Tracker.Domain;

public static class TaskCompletion
{
    public static Result<IReadOnlyList<TaskItem>> Complete(IReadOnlyList<TaskItem> tasks, string id,
        DateOnly completedOn) => TaskLookup.Required(tasks, id).Bind(task =>
        task switch
        {
            { Completed: true } => Result<IReadOnlyList<TaskItem>>.Fail($"Task '{task.Id}' is already complete."),
            _ when completedOn < task.Date => Result<IReadOnlyList<TaskItem>>.Fail(
                $"Task '{task.Id}' is scheduled for {task.Date:yyyy-MM-dd} and cannot be completed earlier."),
            _ => Result<IReadOnlyList<TaskItem>>.Ok(Array.AsReadOnly(tasks.Select(item =>
                item.Id == task.Id ? item with { Completed = true } : item).ToArray()))
        });
}
