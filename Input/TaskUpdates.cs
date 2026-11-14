using System.Globalization;
using System.Text.Json;
using Tracker.Domain;

namespace Tracker.Input;

public static class TaskUpdates
{
    public static Result<IReadOnlyList<TaskItem>> Apply(string path,
        Func<IReadOnlyList<TaskItem>, Result<IReadOnlyList<TaskItem>>> change)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result<IReadOnlyList<TaskItem>>.Fail("A task file path is required.");

        try
        {
            string fullPath = Path.GetFullPath(path);
            using var updateLock = new FileStream(fullPath + ".dailyplanner.lock",
                FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);

            return TaskFile.Load(fullPath).Bind(change).Bind(updated =>
                AtomicTextFile.Save(fullPath, Serialize(updated), "task file").Map(_ => updated));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return Result<IReadOnlyList<TaskItem>>.Fail($"Cannot update task file '{path}': {error.Message}");
        }
    }

    private static string Serialize(IEnumerable<TaskItem> tasks) =>
        JsonSerializer.Serialize(tasks.Select(task => new RawTask(task.Id, task.Title,
            task.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), task.Importance,
            task.Minutes, task.Completed)), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }) + "\n";
}
