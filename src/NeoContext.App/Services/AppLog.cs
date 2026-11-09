namespace NeoContext.App.Services;

/// <summary>
/// A small append-only log in the data folder. One file per day; failures to log are ignored
/// so logging can never crash the app.
/// </summary>
public static class AppLog
{
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.LogDirectory);
            var file = Path.Combine(AppInfo.LogDirectory, $"neocontext-{DateTime.Now:yyyy-MM-dd}.log");
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{(exception is null ? "" : Environment.NewLine + exception)}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(file, line);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
