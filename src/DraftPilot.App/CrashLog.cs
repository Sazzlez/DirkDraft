using System.IO;
using System.Text;

namespace DraftPilot.App;

/// <summary>
/// Writes unhandled exceptions to a file. Without this a start-up failure is just a window that
/// never appears, which is impossible to act on.
/// </summary>
public static class CrashLog
{
    private static readonly Lock Gate = new();

    /// <summary>Runs an action while no crash entry can be appended — the log trim uses this so a
    /// write landing mid-trim is delayed instead of silently discarded.</summary>
    public static void WithLogLock(Action action)
    {
        lock (Gate)
        {
            action();
        }
    }

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DraftPilot", "data", "crash.log");

    /// <summary>
    /// A one-line note without a stack trace, for expected oddities worth a trace (an unreadable
    /// OP.GG answer, a skipped file). Wrapping those in fake exceptions drowned the real crashes.
    /// </summary>
    public static void Note(string context, string message)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            lock (Gate)
            {
                File.AppendAllText(
                    Path,
                    $"--- {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · {context} · {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If even logging fails there is nothing useful left to do.
        }
    }

    public static void Write(string context, Exception exception)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var text = new StringBuilder()
                .AppendLine($"--- {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · {context} ---")
                .AppendLine(exception.ToString())
                .AppendLine()
                .ToString();

            lock (Gate)
            {
                File.AppendAllText(Path, text, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If even logging fails there is nothing useful left to do.
        }
    }
}
