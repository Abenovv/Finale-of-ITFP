using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Tracker.Application;
using Tracker.Domain;
using Tracker.Input;
using Tracker.Native;

namespace Tracker.Web;

internal static class PlannerWeb
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length > 2 || !int.TryParse(args.ElementAtOrDefault(0) ?? "5195", out int port)
            || port is < 1024 or > 65535)
        {
            Console.Error.WriteLine("Usage: DailyPlanner --web [port 1024–65535] [existing task JSON path]");
            return 1;
        }

        string path;
        try
        {
            path = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.GetFullPath("output/web-tasks.json");
            if (args.Length < 2)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path))
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "samples", "tasks.json"), path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Cannot initialize the web task file: {error.Message}");
            return 1;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, port);
            server.Limits.MaxRequestBodySize = 16_384;
        });

        using var assets = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "Web", "assets"));
        await using var web = builder.Build();
        web.Use(async (context, next) =>
        {
            if (context.Request.Host.Port != port || context.Request.Host.Host is not ("127.0.0.1" or "localhost"))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; style-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            await next(context);
        });
        web.UseDefaultFiles(new DefaultFilesOptions { FileProvider = assets });
        web.UseStaticFiles(new StaticFileOptions { FileProvider = assets });

        web.MapGet("/api/tasks", () => Send(TaskFile.Load(path)));
        web.MapPost("/api/tasks", (NewTask input) =>
            Send(TaskDraft.Create(input.Id ?? "", input.Title ?? "", input.Date, input.Importance, input.Minutes)
                .Bind(draft => TaskUpdates.Apply(path, draft.AddTo))));
        web.MapPost("/api/tasks/reschedule", (MoveTask input) =>
            Send(TaskUpdates.Apply(path, tasks => TaskRescheduling.Move(tasks, input.Id ?? "", input.From, input.To))));
        web.MapPost("/api/tasks/complete", (FinishTask input) =>
            Send(TaskUpdates.Apply(path, tasks => TaskCompletion.Complete(tasks, input.Id ?? "", input.On))));
        web.MapPost("/api/plan", (PlanRequest input) => Send(BuildPlan(path, input)));
        web.MapGet("/api/reports", (DateOnly date, int minimum, int capacity) =>
            Send(ReportWorkflow.Generate(path, date, minimum, capacity).Map(reports =>
                new { reports, csv = ReportPresentation.ToCsv(reports) })));

        Console.WriteLine($"DailyPlanner UI  http://127.0.0.1:{port}");
        Console.WriteLine($"Task file        {path}");
        await web.RunAsync();
        return 0;
    }

    private static Result<WebPlan> BuildPlan(string path, PlanRequest request)
    {
        if (request.Minimum is < 1 or > 5 || request.Capacity is < 1 or > 480 || request.Date == default)
            return Result<WebPlan>.Fail("Enter a date, an importance from 1 to 5, and a budget from 1 to 480 minutes.");

        Func<TaskItem, int>? score = request.Policy switch
        {
            "importance" => CurriedScoring.ImportanceFirst,
            "short" => CurriedScoring.ShortTasksFirst,
            _ => null
        };
        if (score is null)
            return Result<WebPlan>.Fail("Choose a scoring policy.");

        if (request.Fit)
            return BudgetWorkflow.Run(path, request.Date, request.Minimum, request.Capacity, score, new NativeLoadAdapter())
                .Map(preview => new WebPlan(preview.Confirmation.Reference, preview.Confirmation.Session.Plan,
                    preview.Confirmation.Session.RemainingMinutes, preview.Deferred, preview.LoadPercent));

        return TaskFile.Load(path)
            .Map(tasks => TaskPipeline.CreatePlan(tasks, request.Date, request.Minimum, score))
            .Bind(plan => TaskRequestWorkflow.PrepareAndConfirm(PlanningState.Start(plan, request.Capacity)))
            .Map(confirmation => new WebPlan(confirmation.Reference, confirmation.Session.Plan,
                confirmation.Session.RemainingMinutes, [], null));
    }

    private static IResult Send<T>(Result<T> result) => result switch
    {
        Result<T>.Success found => Results.Ok(found.Value),
        Result<T>.Failure failed => Results.BadRequest(new { error = failed.Error }),
        _ => throw new InvalidOperationException("Unknown workflow result.")
    };

    private sealed record NewTask(string? Id, string? Title, DateOnly Date, int Importance, int Minutes);
    private sealed record MoveTask(string? Id, DateOnly From, DateOnly To);
    private sealed record FinishTask(string? Id, DateOnly On);
    private sealed record PlanRequest(DateOnly Date, int Minimum, int Capacity, string? Policy, bool Fit);
    private sealed record WebPlan(string Reference, DailyPlan Plan, int Remaining, IReadOnlyList<RankedTask> Deferred, int? LoadPercent);
}
