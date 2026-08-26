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

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DraftPilot", "data", "crash.log");

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
