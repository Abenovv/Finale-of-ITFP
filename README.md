# DailyPlanner

<img src="Web/assets/dailyplanner-icon.png" width="72" height="72" alt="DailyPlanner calendar icon">

DailyPlanner helps students organize tasks and make a plan that fits their available study time.

**IT-2511** · Yerkebulan Abenov, Nurbergen Assan, Tumargul Amanalina, Khalid Ahmad
**Repository** · [Abenovv/Finale-of-ITFP](https://github.com/Abenovv/Finale-of-ITFP) · Branch `main`

Source archive commit: `$Format:%H$`. In a Git checkout, use `git rev-parse HEAD`.

## Run

Use .NET SDK 10.0.401. Run these commands from the folder containing `DailyPlanner.sln`:

```sh
dotnet build DailyPlanner.sln --disable-build-servers --maxcpucount:1
dotnet run --project Tests/DailyPlanner.Tests.csproj --no-build
dotnet run --project DailyPlanner.csproj --no-build -- --web
```

Open [DailyPlanner](http://127.0.0.1:5195/). The browser interface uses HTML, CSS, and JavaScript over a local ASP.NET Core server. The header and browser tab use the same transparent PNG. Yerkebulan Abenov is assigned the browser screens and icon integration. Khalid Ahmad is assigned the ASP.NET Core routes and their connection to the C# workflows.

Windows x64 includes the native DLL. Rebuild it with MSYS2 UCRT64 GCC 14.2.0 using `powershell -NoProfile -File native/build.ps1`. On macOS, install Apple Command Line Tools and the .NET SDK matching your architecture, then run `sh native/build.sh` before building. The same shell script builds the Linux library with a C compiler. Build and runtime verification was performed on Windows x64.

## Workflows

| Operation | Rule |
| --- | --- |
| Add a task | Validate the title, ID, priority, and duration. Reject duplicate IDs. |
| Move a task | Verify its current date and move an open task to a later date. |
| Complete a task | Mark an open task complete on or after its due date. |
| Confirm a daily plan | Rank all eligible tasks and confirm when they fit the budget. |
| Fit a shorter plan | Keep tasks in rank order and defer each one that exceeds the remaining budget. |
| Generate reports | Compare both focus rankings and view the workload for the selected day and the next day. Download the displayed results as CSV. |

Tasks, Plan, and Reports share one date, budget, and minimum priority. The browser remembers these settings. The first run creates `output/web-tasks.json` from the sample. Browser edits persist there. Pass `--web 5200 path/to/tasks.json` to use a different port and an existing JSON file.

```sh
dotnet run --project DailyPlanner.csproj --no-build -- --fit samples/tasks.json 2026-10-13 importance 3 55
dotnet run --project DailyPlanner.csproj --no-build -- --reports samples/tasks.json output/planner-report.csv 180 2026-10-13 3
dotnet run --project DailyPlanner.csproj --no-build -- --error-demo
```

The 55-minute plan fits T2 and T7 in 50 minutes, defers T1 and T3, and reports 91% use. At a 180-minute budget, the two workload rows report 75% and 12%. The error demo includes a missing task, a rejected confirmation, and an overloaded plan.

## Implementation

| Requirement | Code |
| --- | --- |
| Immutable data | `Domain/Models.cs` relates `TaskItem` to `DailyPlan` through ranked tasks. A plan copies its input collection. |
| Pipeline and policies | `TaskPipeline.CreatePlan` selects, scores, and ranks. `ScoreTasks` calls the chosen delegate. `CurriedScoring.Create` fixes an importance weight of 10 and a short-task bonus window of 0 or 60 minutes. Sample task T3 scores 30 or 80. |
| Validation and states | `TaskDraft.Create` and `ValidatedPlanningSession.Create` guard construction. `PlanningWorkflow` matches Draft, Prepared, and Confirmed and rejects invalid transitions. |
| Explicit errors | `TaskLookup.Find` returns `Option`. `BudgetWorkflow.Run` and `TaskRequestWorkflow.Request` compose `Result` with `Map` and `Bind`. Failure skips later steps. |
| LINQ and lazy scoring | `PlannerReports` uses `EligibleOn` and `RankForPlanning` for focus rankings. Ties use ordinal ID. Workload groups by date using `GroupBy`, `Count`, and `Sum`. `LazyPlanning.ScoreEligible` uses `yield return`, and the focus report materializes scores with `ToArray` before ranking. |
| Persistence | `TaskFile` and `TaskParser` validate the JSON. `TaskUpdates` holds an exclusive sidecar lock during changes. `AtomicTextFile` closes the temporary writer before replacement and cleans up on failure. |
| Native C | `native/planner_native.c` exports `planner_load_percent`. The C# binding uses P/Invoke with C calling convention and 32-bit integers. The adapter surfaces errors as `Result`. The workload and fitted plan use the returned percentage. |

JSON entries require unique IDs, a title, an ISO date, priority 1–5, duration 0–480, and a completion flag. Failed edits preserve the source file. The lock handle is disposed after each update. The empty sidecar stays in place so later writers reuse the same lock path. Completion stores the status rather than the supplied date.

## Tests and responsibilities

The Windows x64 build completed with zero warnings and errors. **124/124 tests passed**, covering validation, sequencing, workflow success and failure, empty data, file handling, lazy enumeration, reports, and real and controlled-failure native calls. The browser checks also covered task updates, planning, shared report settings, and CSV download.

| Member | Assigned final workflow | Tests |
| --- | --- | --- |
| Yerkebulan Abenov | Task capture, JSON updates, and browser screens | `CaptureTests.cs` |
| Nurbergen Assan | Rescheduling | `RescheduleTests.cs` |
| Tumargul Amanalina | Completion and workload updates | `CompletionTests.cs` |
| Khalid Ahmad | Budget fitting, native utilization, and browser integration | `BudgetTests.cs` |

DailyPlanner stores data locally and keeps no plan history. The budget fitter follows rank order and can skip an oversized task. The project builds on Assignments 5–8 and uses the .NET and C standard libraries.
