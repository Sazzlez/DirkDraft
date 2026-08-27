namespace DraftPilot.Core.Config;

/// <summary>
/// Keeps the data directory from silting up. Runs in the background at start-up and after a data
/// update; everything here is best-effort — a locked file waits for the next run.
/// <para>
/// What is deliberately NOT cleaned: champion, item, rune and spell icons. They are small, do not
/// go stale (ids are permanent), and deleting them would only mean downloading them again.
/// </para>
/// </summary>
public static class Maintenance
{
    /// <summary>Logs above this size get trimmed…</summary>
    private const long MaxLogBytes = 256 * 1024;

    /// <summary>…down to roughly this much of their newest content.</summary>
    private const int KeepLogBytes = 128 * 1024;

    /// <summary>
    /// Trims a grow-forever log file to its newest entries once it passes the cap. The cut lands
    /// on a line boundary, so the file always starts with a complete entry.
    /// <para>
    /// In place over ONE exclusive stream, not read-then-replace: the replace variant lost every
    /// entry a logger appended between the read and the move, and an interrupted run left a
    /// <c>.tmp</c> carcass behind. FileShare.None also means a concurrent logger simply fails its
    /// append (which the loggers already tolerate) instead of racing us.
    /// </para>
    /// </summary>
    public static void TrimLog(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxLogBytes)
                return;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var tail = new byte[KeepLogBytes];
            stream.Seek(-KeepLogBytes, SeekOrigin.End);
            stream.ReadExactly(tail);

            var start = Array.IndexOf(tail, (byte)'\n') + 1;

            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(tail.AsSpan(start));
            stream.SetLength(KeepLogBytes - start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another instance may hold the log; next start trims it.
        }
    }

    /// <summary>Removes leftover <c>.tmp</c> files from interrupted atomic writes and downloads.</summary>
    public static int SweepTempFiles(params string[] directories)
    {
        var removed = 0;

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.tmp"))
            {
                try
                {
                    // A .tmp being written right now is younger than a minute; leave it alone.
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < TimeSpan.FromMinutes(1))
                        continue;

                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still in use; next run.
                }
            }
        }

        return removed;
    }
}
