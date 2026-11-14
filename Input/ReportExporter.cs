using System.Text;
using Tracker.Domain;

namespace Tracker.Input;

public static class ReportExporter
{
    public static Result<string> Save(string destination, string csv, string? protectedInputPath = null)
    {
        return AtomicTextFile.Save(destination, csv, "report", protectedInputPath);
    }
}

internal static class AtomicTextFile
{
    public static Result<string> Save(string destination, string contents, string document, string? protectedInputPath = null)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (string.IsNullOrWhiteSpace(destination))
            return Result<string>.Fail($"A {document} destination is required.");

        string? temporaryPath = null;
        string? savedPath = null;
        string? errorMessage = null;
        try
        {
            string fullPath = Path.GetFullPath(destination);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (protectedInputPath is not null
                && string.Equals(fullPath, Path.GetFullPath(protectedInputPath), comparison))
                return Result<string>.Fail($"Cannot export {document} to '{destination}': it is the input data file.");

            string? directory = Path.GetDirectoryName(fullPath);
            if (directory is null)
                return Result<string>.Fail($"Cannot export {document} to '{destination}': a file path is required.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".planner-report-{Guid.NewGuid():N}.tmp");

            // The exporter owns both handles. Disposal completes before the destination is replaced.
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(contents);
            }

            // A temporary sibling keeps the final replacement on the same filesystem.
            File.Move(temporaryPath, fullPath, overwrite: true);
            temporaryPath = null;
            savedPath = fullPath;
        }
        catch (Exception error) when (IsFileFailure(error))
        {
            errorMessage = $"Cannot export {document} to '{destination}': {error.Message}";
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception cleanupError) when (IsFileFailure(cleanupError))
                {
                    errorMessage = $"{errorMessage} Temporary report '{temporaryPath}' could not be removed: {cleanupError.Message}";
                }
            }
        }

        if (errorMessage is not null)
            return Result<string>.Fail(errorMessage);
        if (savedPath is not null)
            return Result<string>.Ok(savedPath);
        throw new InvalidOperationException("Export finished without an outcome.");
    }

    private static bool IsFileFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
