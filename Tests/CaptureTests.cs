using Tracker.Domain;
using Tracker.Input;

namespace Tracker.Tests;

internal static partial class Program
{
    private static IEnumerable<(string Name, Action Run)> CaptureCases()
    {
        yield return ("Capture validates required fields and duration bounds", () =>
        {
            Assert(TaskDraft.Create(" ", "Lab", Day, 2, 0) is Result<TaskDraft>.Failure, "Blank ID accepted.");
            Assert(TaskDraft.Create("N1", " ", Day, 2, 0) is Result<TaskDraft>.Failure, "Blank title accepted.");
            Assert(TaskDraft.Create("N1", "Lab", Day, 6, 0) is Result<TaskDraft>.Failure, "Invalid importance accepted.");
            Assert(TaskDraft.Create("N1", "Lab", Day, 2, 481) is Result<TaskDraft>.Failure, "Invalid duration accepted.");
        });

        yield return ("Capture persists a new validated reminder and rejects a duplicate without changing the file", () =>
            WithExportDirectory(directory =>
            {
                string path = Path.Combine(directory, "tasks.json");
                File.WriteAllText(path, "[]");
                var draft = TaskDraft.Create(" N1 ", " Check lab ", Day, 3, 0);
                var result = draft.Bind(value => TaskUpdates.Apply(path, value.AddTo));
                var expected = new TaskItem("N1", "Check lab", Day, 3, 0, false);
                Assert(TaskFile.Load(path) is Result<IReadOnlyList<TaskItem>>.Success loaded
                    && loaded.Value.SequenceEqual([expected]), "Persisted data did not round-trip.");
                Assert(result is Result<IReadOnlyList<TaskItem>>.Success, "Capture failed.");
                string previous = File.ReadAllText(path);
                Assert(draft.Bind(value => TaskUpdates.Apply(path, value.AddTo)) is Result<IReadOnlyList<TaskItem>>.Failure,
                    "Duplicate task was accepted.");
                Equal(previous, File.ReadAllText(path));
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }));

        yield return ("Task updates skip business logic on malformed data and while another update holds the lock", () =>
            WithExportDirectory(directory =>
            {
                string path = Path.Combine(directory, "tasks.json");
                File.WriteAllText(path, "[");
                int calls = 0;
                Result<IReadOnlyList<TaskItem>> Change(IReadOnlyList<TaskItem> tasks)
                {
                    calls++;
                    return Result<IReadOnlyList<TaskItem>>.Ok(tasks);
                }

                Assert(TaskUpdates.Apply(path, Change) is Result<IReadOnlyList<TaskItem>>.Failure, "Malformed file accepted.");
                Equal("[", File.ReadAllText(path));
                File.WriteAllText(path, "[]");
                using var held = File.Open(path + ".dailyplanner.lock", FileMode.Open, FileAccess.Write, FileShare.None);
                Assert(TaskUpdates.Apply(path, Change) is Result<IReadOnlyList<TaskItem>>.Failure, "Concurrent update accepted.");
                Equal("[]", File.ReadAllText(path));
                Equal(0, calls);
            }));
    }
}
