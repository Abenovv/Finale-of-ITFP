using Tracker.Domain;
using Tracker.Input;

namespace Tracker.Tests;

internal static partial class Program
{
    private static IEnumerable<(string Name, Action Run)> CompletionCases()
    {
        yield return ("Completing a task removes its minutes from reports and preserves the input snapshot", () =>
        {
            var tasks = Sample();
            var result = TaskCompletion.Complete(tasks, "T1", Day);
            Assert(result is Result<IReadOnlyList<TaskItem>>.Success, "Completion failed.");
            var updated = ((Result<IReadOnlyList<TaskItem>>.Success)result).Value;
            Assert(!tasks[0].Completed && updated[0].Completed, "The new snapshot must own the change.");
            Sequence(tasks.Skip(1), updated.Skip(1));
            var workload = PlannerReports.WorkloadByDay(updated, Day, Day).Single();
            Equal(75, workload.TotalMinutes);
            Equal(4, workload.Tasks);
        });

        yield return ("Completion rejects future scheduled tasks, missing tasks, and repeated completion", () =>
        {
            var tasks = Sample();
            Assert(TaskCompletion.Complete(tasks, "T6", Day) is Result<IReadOnlyList<TaskItem>>.Failure,
                "Future task was completed.");
            Assert(TaskCompletion.Complete(tasks, "missing", Day) is Result<IReadOnlyList<TaskItem>>.Failure,
                "Missing task was completed.");
            Assert(TaskCompletion.Complete(tasks, "T5", Day) is Result<IReadOnlyList<TaskItem>>.Failure,
                "Completed task was accepted.");
        });

        yield return ("Completion persists and a repeat leaves task data unchanged", () =>
            WithExportDirectory(directory =>
            {
                string path = Path.Combine(directory, "tasks.json");
                File.Copy("samples/tasks.json", path);
                Assert(TaskUpdates.Apply(path, tasks => TaskCompletion.Complete(tasks, "T1", Day))
                    is Result<IReadOnlyList<TaskItem>>.Success, "Completion was not saved.");
                Assert(TaskFile.Load(path) is Result<IReadOnlyList<TaskItem>>.Success loaded
                    && loaded.Value.Single(task => task.Id == "T1").Completed, "Saved status did not round-trip.");
                string previous = File.ReadAllText(path);
                Assert(TaskUpdates.Apply(path, tasks => TaskCompletion.Complete(tasks, "T1", Day))
                    is Result<IReadOnlyList<TaskItem>>.Failure, "Repeat completion was accepted.");
                Equal(previous, File.ReadAllText(path));
            }));
    }
}
