using System.Diagnostics;
using NeoContext.Core.Results;

namespace NeoContext.Registry;

/// <summary>
/// Exports keys to .reg files with the built-in <c>reg.exe</c>, so a deleted entry can be restored
/// by double-clicking its backup. NeoContext exports every registry entry before deleting it.
/// </summary>
public sealed class RegistryExporter(string backupDirectory)
{
    public string BackupDirectory { get; } = backupDirectory;

    /// <summary>Writes <c>&lt;timestamp&gt; &lt;key name&gt;.reg</c> and returns its path.</summary>
    public Result<string> Export(RegistryPath path)
    {
        Directory.CreateDirectory(BackupDirectory);
        var safeName = string.Concat(path.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var file = Path.Combine(BackupDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss} {safeName}.reg");

        var start = new ProcessStartInfo("reg.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("export");
        start.ArgumentList.Add(path.ToString());
        start.ArgumentList.Add(file);
        start.ArgumentList.Add("/y");

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? file
            : Result<string>.Failure(ErrorKind.Unexpected, $"Could not back up {path}: {error.Trim()}");
    }

    /// <summary>Writes a backup made by <see cref="Export"/> back into the registry with <c>reg.exe import</c>.</summary>
    public static Result Import(string file)
    {
        if (!File.Exists(file))
        {
            return Result.Failure(ErrorKind.NotFound, $"Backup file not found: {file}");
        }

        var start = new ProcessStartInfo("reg.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("import");
        start.ArgumentList.Add(file);

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? Result.Success()
            : Result.Failure(ErrorKind.AccessDenied, $"Could not restore {file}: {error.Trim()}");
    }
}
