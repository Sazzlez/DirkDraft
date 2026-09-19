using DraftPilot.Core.Config;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta;

/// <summary>
/// Fetches champion portraits from Data Dragon into the local icon cache.
/// <para>
/// Only missing files are fetched. Portraits are effectively static per champion, so after the
/// first update this stage costs nothing but a directory listing, and a newly released champion
/// picks up its icon on the next update by itself.
/// </para>
/// </summary>
public sealed class IconDownloader(HttpClient http)
{
    private const int MaxConcurrency = 4;

    /// <summary>Downloads whatever is not already on disk. Returns how many files were added.</summary>
    public async Task<int> DownloadMissingAsync(
        IReadOnlyList<ChampionEntry> champions,
        string patch,
        List<string> warnings,
        IProgress<BuildProgress>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patch))
        {
            warnings.Add("Ohne Patch-Version keine Champ-Icons.");
            return 0;
        }

        Directory.CreateDirectory(AppPaths.IconDirectory);

        var missing = champions
            .Where(champion => !string.IsNullOrEmpty(champion.Key) && !IsUsableFile(PathFor(champion.Id)))
            .ToList();

        if (missing.Count == 0)
            return 0;

        var added = 0;
        var failed = 0;
        var done = 0;
        var gate = new SemaphoreSlim(MaxConcurrency);
        var sync = new Lock();

        await Task.WhenAll(missing.Select(async champion =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var ok = await TryDownloadAsync(champion, patch, ct).ConfigureAwait(false);

                lock (sync)
                {
                    if (ok)
                        added++;
                    else
                        failed++;

                    progress?.Report(new BuildProgress("Icons", ++done, missing.Count));
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        if (failed > 0)
            warnings.Add($"{failed} Champ-Icons konnten nicht geladen werden.");

        return added;
    }

    /// <summary>
    /// Exists AND is plausibly a real image. A zero-byte or truncated file (disk full, killed
    /// mid-write) would otherwise never be repaired — the plain Exists check skipped it forever.
    /// </summary>
    private static bool IsUsableFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length >= 256;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<bool> TryDownloadAsync(ChampionEntry champion, string patch, CancellationToken ct)
    {
        var url = $"https://ddragon.leagueoflegends.com/cdn/{patch}/img/champion/{champion.Key}.png";
        var target = PathFor(champion.Id);

        // Process-unique temp name: the app and the CLI tool can both be downloading, and a
        // shared name meant sharing violations or moving each other's half-written files.
        var temporary = $"{target}.{Environment.ProcessId}.tmp";

        try
        {
            var bytes = await http.GetByteArrayAsync(url, ct).ConfigureAwait(false);

            // A truncated or error-page response would render as a broken tile forever.
            if (bytes.Length < 256)
                return false;

            await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelling the update must stay a cancellation — but not leave the .tmp behind.
            TryDelete(temporary);
            throw;
        }
        catch (OperationCanceledException)
        {
            // NOT a user cancellation: HttpClient reports its own request timeout as a
            // TaskCanceledException. Rethrown, one slow icon killed the whole minutes-long
            // update through Task.WhenAll; a timeout costs exactly this one file.
            TryDelete(temporary);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return false;
        }
    }

    /// <summary>Where a champion's portrait lives. Keyed by id, so a rename cannot orphan it.</summary>
    public static string PathFor(int championId)
        => Path.Combine(AppPaths.IconDirectory, $"{championId}.png");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftover temp file; harmless.
        }
    }
}
