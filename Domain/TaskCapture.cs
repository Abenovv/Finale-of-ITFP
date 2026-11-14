namespace Tracker.Domain;

public sealed class TaskDraft
{
    private readonly TaskItem task;

    private TaskDraft(TaskItem task) => this.task = task;

    public static Result<TaskDraft> Create(string id, string title, DateOnly date, int importance, int minutes)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            return Result<TaskDraft>.Fail("A task ID and title are required.");
        if (importance is < 1 or > 5)
            return Result<TaskDraft>.Fail("Task importance must be from 1 to 5.");
        if (minutes is < 0 or > 480)
            return Result<TaskDraft>.Fail("Task minutes must be from 0 to 480.");

        return Result<TaskDraft>.Ok(new(new TaskItem(id.Trim(), title.Trim(), date, importance, minutes, false)));
    }

    public Result<IReadOnlyList<TaskItem>> AddTo(IReadOnlyList<TaskItem> tasks) =>
        TaskLookup.Find(tasks, task.Id) switch
        {
            Option<TaskItem>.Some => Result<IReadOnlyList<TaskItem>>.Fail($"Task '{task.Id}' already exists."),
            Option<TaskItem>.None => Result<IReadOnlyList<TaskItem>>.Ok(Array.AsReadOnly(tasks.Append(task).ToArray())),
            _ => throw new InvalidOperationException("Unknown lookup outcome.")
        };
}
