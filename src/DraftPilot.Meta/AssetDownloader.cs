using System.Text.Json;
using DraftPilot.Core.Config;
using DraftPilot.Core.Data;

namespace DraftPilot.Meta;

/// <summary>
/// Fetches item, rune and summoner-spell assets from Data Dragon: icons plus localised names.
/// <para>
/// Same discipline as <see cref="IconDownloader"/>: only what is missing is fetched, files are
/// written atomically, and a failure costs a warning, never the update. Rune and spell assets come
/// with the big update (~85 small files, once per install); item icons are fetched on demand with
/// the build plan that references them (~10 files per new matchup).
/// </para>
/// </summary>
public sealed class AssetDownloader(HttpClient http)
{
    private const int MaxConcurrency = 4;

    private const string Cdn = "https://ddragon.leagueoflegends.com/cdn";

    /// <summary>Downloads missing item icons for the given ids. Returns how many files were added.</summary>
    public async Task<int> DownloadItemIconsAsync(IEnumerable<int> itemIds, string patch, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patch))
            return 0;

        Directory.CreateDirectory(AppPaths.ItemIconDirectory);

        var missing = itemIds
            .Distinct()
            .Where(id => id > 0 && !IsUsableFile(ItemIconPath(id)))
            .ToList();

        var added = 0;

        foreach (var id in missing)
        {
            if (await TryDownloadAsync($"{Cdn}/{patch}/img/item/{id}.png", ItemIconPath(id), ct).ConfigureAwait(false))
                added++;
        }

        return added;
    }

    /// <summary>
    /// On-demand counterpart to the big-update stage: fetches exactly the rune and spell icons a
    /// build plan references, when they are missing. Exists so somebody who never ran the big
    /// update is not stuck with text chips forever — the icon paths come from two small Data
    /// Dragon files that are fetched once per call (locale irrelevant for images).
    /// Returns how many files were added.
    /// </summary>
    public async Task<int> DownloadRuneAndSpellIconsAsync(
        IEnumerable<int> runeIds,
        IEnumerable<int> spellIds,
        string patch,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patch))
            return 0;

        Directory.CreateDirectory(AppPaths.RuneIconDirectory);
        Directory.CreateDirectory(AppPaths.SpellIconDirectory);

        var missingRunes = runeIds.Distinct()
            .Where(id => id > 0 && !IsUsableFile(Path.Combine(AppPaths.RuneIconDirectory, $"{id}.png")))
            .ToHashSet();
        var missingSpells = spellIds.Distinct()
            .Where(id => id > 0 && !IsUsableFile(Path.Combine(AppPaths.SpellIconDirectory, $"{id}.png")))
            .ToHashSet();

        if (missingRunes.Count == 0 && missingSpells.Count == 0)
            return 0;

        var added = 0;

        if (missingRunes.Count > 0)
        {
            try
            {
                using var runes = JsonDocument.Parse(
                    await http.GetStringAsync($"{Cdn}/{patch}/data/en_US/runesReforged.json", ct).ConfigureAwait(false));

                var icons = new Dictionary<int, string>();
                var discardedNames = new Dictionary<int, string>();
                foreach (var style in runes.RootElement.EnumerateArray())
                {
                    CollectRune(style, discardedNames, icons);

                    if (!style.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var slot in slots.EnumerateArray())
                    {
                        if (!slot.TryGetProperty("runes", out var slotRunes) || slotRunes.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var rune in slotRunes.EnumerateArray())
                            CollectRune(rune, discardedNames, icons);
                    }
                }

                foreach (var id in missingRunes)
                {
                    if (icons.TryGetValue(id, out var icon) && icon.Length > 0
                        && await TryDownloadAsync($"{Cdn}/img/{icon}", Path.Combine(AppPaths.RuneIconDirectory, $"{id}.png"), ct).ConfigureAwait(false))
                    {
                        added++;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                // Icons are decoration; the text chips carry the build until the next chance.
            }
        }

        if (missingSpells.Count > 0)
        {
            try
            {
                using var spells = JsonDocument.Parse(
                    await http.GetStringAsync($"{Cdn}/{patch}/data/en_US/summoner.json", ct).ConfigureAwait(false));

                if (spells.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    foreach (var spell in data.EnumerateObject())
                    {
                        if (!spell.Value.TryGetProperty("key", out var key)
                            || key.ValueKind != JsonValueKind.String
                            || !int.TryParse(key.GetString(), out var id)
                            || !missingSpells.Contains(id))
                        {
                            continue;
                        }

                        if (spell.Value.TryGetProperty("image", out var image)
                            && image.ValueKind == JsonValueKind.Object
                            && image.TryGetProperty("full", out var full)
                            && full.ValueKind == JsonValueKind.String
                            && full.GetString() is { Length: > 0 } icon
                            && await TryDownloadAsync($"{Cdn}/{patch}/img/spell/{icon}", Path.Combine(AppPaths.SpellIconDirectory, $"{id}.png"), ct).ConfigureAwait(false))
                        {
                            added++;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                // Same as above.
            }
        }

        return added;
    }

    /// <summary>
    /// The big-update stage: localised names for items, runes and spells, plus the rune and spell
    /// icons. Everything already on disk for this patch is skipped.
    /// </summary>
    public async Task DownloadRuneAndSpellAssetsAsync(
        string patch,
        string language,
        List<string> warnings,
        IProgress<BuildProgress>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(patch))
        {
            warnings.Add("Ohne Patch-Version keine Runen- und Item-Namen.");
            return;
        }

        var names = new AssetNames { Patch = patch, Language = language };

        // Runes: names AND icon paths come from the same file.
        var runeIcons = new Dictionary<int, string>();

        try
        {
            using var runes = JsonDocument.Parse(
                await http.GetStringAsync($"{Cdn}/{patch}/data/{language}/runesReforged.json", ct).ConfigureAwait(false));

            foreach (var style in runes.RootElement.EnumerateArray())
            {
                CollectRune(style, names.Runes, runeIcons);

                if (!style.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var slot in slots.EnumerateArray())
                {
                    if (!slot.TryGetProperty("runes", out var slotRunes) || slotRunes.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var rune in slotRunes.EnumerateArray())
                        CollectRune(rune, names.Runes, runeIcons);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            warnings.Add("Runen-Daten konnten nicht geladen werden.");
        }

        // Stat shards have no entry in runesReforged; the curated translation covers them.
        foreach (var shard in (int[])[5001, 5002, 5003, 5005, 5007, 5008, 5010, 5011, 5013])
            names.Runes.TryAdd(shard, StatShards.NameOf(shard));

        var spellIcons = new Dictionary<int, string>();

        try
        {
            using var spells = JsonDocument.Parse(
                await http.GetStringAsync($"{Cdn}/{patch}/data/{language}/summoner.json", ct).ConfigureAwait(false));

            // TryGetProperty everywhere: Data Dragon has shipped entries with missing fields
            // before, and one odd spell must cost a warning, not the whole update.
            if (spells.RootElement.TryGetProperty("data", out var spellData) && spellData.ValueKind == JsonValueKind.Object)
            {
                foreach (var spell in spellData.EnumerateObject())
                {
                    if (!spell.Value.TryGetProperty("key", out var key)
                        || key.ValueKind != JsonValueKind.String
                        || !int.TryParse(key.GetString(), out var id))
                    {
                        continue;
                    }

                    if (spell.Value.TryGetProperty("name", out var spellName) && spellName.ValueKind == JsonValueKind.String)
                        names.Spells[id] = spellName.GetString() ?? string.Empty;

                    if (spell.Value.TryGetProperty("image", out var image)
                        && image.ValueKind == JsonValueKind.Object
                        && image.TryGetProperty("full", out var full)
                        && full.ValueKind == JsonValueKind.String)
                    {
                        spellIcons[id] = full.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            warnings.Add("Summoner-Spell-Daten konnten nicht geladen werden.");
        }

        try
        {
            // ~1.1 MB once per patch; reduced to the id → name map and dropped.
            using var items = JsonDocument.Parse(
                await http.GetStringAsync($"{Cdn}/{patch}/data/{language}/item.json", ct).ConfigureAwait(false));

            if (items.RootElement.TryGetProperty("data", out var itemData) && itemData.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in itemData.EnumerateObject())
                {
                    if (int.TryParse(item.Name, out var id)
                        && item.Value.TryGetProperty("name", out var itemName)
                        && itemName.ValueKind == JsonValueKind.String)
                    {
                        names.Items[id] = itemName.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            warnings.Add("Item-Namen konnten nicht geladen werden.");
        }

        // Merge with what is already on disk: an offline update must never overwrite a good
        // localised names file with an almost-empty one. TryAdd — fresh entries win.
        var existing = AssetNames.Load(language);
        foreach (var (id, name) in existing.Items)
            names.Items.TryAdd(id, name);
        foreach (var (id, name) in existing.Runes)
            names.Runes.TryAdd(id, name);
        foreach (var (id, name) in existing.Spells)
            names.Spells.TryAdd(id, name);

        try
        {
            names.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Namen-Datei konnte nicht gespeichert werden.");
        }

        // Rune icons live under a patchless URL; spell icons under the patch like everything else.
        Directory.CreateDirectory(AppPaths.RuneIconDirectory);
        Directory.CreateDirectory(AppPaths.SpellIconDirectory);

        var downloads = new List<(string Url, string Target)>();

        foreach (var (id, icon) in runeIcons)
        {
            var target = Path.Combine(AppPaths.RuneIconDirectory, $"{id}.png");
            if (!IsUsableFile(target) && icon.Length > 0)
                downloads.Add(($"{Cdn}/img/{icon}", target));
        }

        foreach (var (id, icon) in spellIcons)
        {
            var target = Path.Combine(AppPaths.SpellIconDirectory, $"{id}.png");
            if (!IsUsableFile(target) && icon.Length > 0)
                downloads.Add(($"{Cdn}/{patch}/img/spell/{icon}", target));
        }

        if (downloads.Count == 0)
            return;

        var failed = 0;
        var done = 0;
        var gate = new SemaphoreSlim(MaxConcurrency);
        var sync = new Lock();

        await Task.WhenAll(downloads.Select(async download =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var ok = await TryDownloadAsync(download.Url, download.Target, ct).ConfigureAwait(false);

                lock (sync)
                {
                    if (!ok)
                        failed++;

                    progress?.Report(new BuildProgress("Runen/Spells", ++done, downloads.Count));
                }
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        if (failed > 0)
            warnings.Add($"{failed} Runen-/Spell-Icons konnten nicht geladen werden.");
    }

    private static void CollectRune(JsonElement rune, Dictionary<int, string> names, Dictionary<int, string> icons)
    {
        if (!rune.TryGetProperty("id", out var idProperty) || !idProperty.TryGetInt32(out var id))
            return;

        if (rune.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            names[id] = name.GetString() ?? string.Empty;

        if (rune.TryGetProperty("icon", out var icon) && icon.ValueKind == JsonValueKind.String)
            icons[id] = icon.GetString() ?? string.Empty;
    }

    public static string ItemIconPath(int itemId)
        => Path.Combine(AppPaths.ItemIconDirectory, $"{itemId}.png");

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

    private async Task<bool> TryDownloadAsync(string url, string target, CancellationToken ct)
    {
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
