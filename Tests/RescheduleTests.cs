using Tracker.Domain;
using Tracker.Input;

namespace Tracker.Tests;

internal static partial class Program
{
    private static IEnumerable<(string Name, Action Run)> RescheduleCases()
    {
        yield return ("Rescheduling changes only the chosen task and removes it from the original day's pipeline", () =>
        {
            var tasks = Sample();
            var result = TaskRescheduling.Move(tasks, "T1", Day, Day.AddDays(1));
            Assert(result is Result<IReadOnlyList<TaskItem>>.Success, "Reschedule failed.");
            var moved = ((Result<IReadOnlyList<TaskItem>>.Success)result).Value;
            Equal(Day, tasks[0].Date);
            Equal(tasks[0] with { Date = Day.AddDays(1) }, moved[0]);
            Sequence(tasks.Skip(1), moved.Skip(1));
            Sequence(new[] { "T2", "T7", "T3" }, TaskPipeline.CreatePlan(moved, Day, 3,
                ScoringPolicies.ImportanceFirst).Tasks.Select(item => item.Task.Id));
        });

        yield return ("Rescheduling rejects stale dates, reverse moves, completed tasks, and missing IDs", () =>
        {
            var tasks = Sample();
            Assert(TaskRescheduling.Move(tasks, "T1", Day.AddDays(-1), Day.AddDays(1)) is Result<IReadOnlyList<TaskItem>>.Failure,
                "Stale date was accepted.");
            Assert(TaskRescheduling.Move(tasks, "T1", Day, Day.AddDays(-1)) is Result<IReadOnlyList<TaskItem>>.Failure,
                "Reverse move was accepted.");
            Assert(TaskRescheduling.Move(tasks, "T5", Day, Day.AddDays(1)) is Result<IReadOnlyList<TaskItem>>.Failure,
                "Completed task was moved.");
            Assert(TaskRescheduling.Move(tasks, "missing", Day, Day.AddDays(1)) is Result<IReadOnlyList<TaskItem>>.Failure,
                "Missing task was moved.");
        });

        yield return ("Rescheduling persists the target date and a stale retry leaves the file unchanged", () =>
            WithExportDirectory(directory =>
            {
                string path = Path.Combine(directory, "tasks.json");
                File.Copy("samples/tasks.json", path);
                var target = Day.AddDays(2);
                Assert(TaskUpdates.Apply(path, tasks => TaskRescheduling.Move(tasks, "T1", Day, target))
                    is Result<IReadOnlyList<TaskItem>>.Success, "Reschedule was not saved.");
                Assert(TaskFile.Load(path) is Result<IReadOnlyList<TaskItem>>.Success loaded
                    && loaded.Value.Single(task => task.Id == "T1").Date == target, "New date is missing.");
                string previous = File.ReadAllText(path);
                Assert(TaskUpdates.Apply(path, tasks => TaskRescheduling.Move(tasks, "T1", Day, target))
                    is Result<IReadOnlyList<TaskItem>>.Failure, "Stale retry was accepted.");
                Equal(previous, File.ReadAllText(path));
            }));
    }
}
