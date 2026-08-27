using DraftPilot.Core.Data;
using DraftPilot.Core.Lcu.Models;

namespace DraftPilot.Core.Lcu;

/// <summary>What came of an import attempt, in words the status line can show.</summary>
public sealed record RuneImportResult(bool Ok, string Message);

/// <summary>
/// Writes a build plan's rune page into the client.
/// <para>
/// This is the tool's only write to the client, and it follows one rule: DraftPilot deletes only
/// pages it created itself, recognisable by the <see cref="PagePrefix"/>. When the account's page
/// limit forces replacing a foreign page, the user is asked first — by name — through the
/// <c>confirmReplace</c> callback, and a "no" aborts the import before anything was deleted.
/// Everything is decided up front; deletes only start once the outcome is settled.
/// </para>
/// </summary>
public static class RuneImporter
{
    /// <summary>Prefix marking pages as ours. Also how a re-import finds the pages to replace.</summary>
    public const string PagePrefix = "DirkRunen";

    /// <summary>The tool's earlier working title; pages from before the rename still count as ours.</summary>
    private const string LegacyPagePrefix = "DraftPilot";

    /// <summary>Whether this page was created by this tool, under either name.</summary>
    public static bool IsOurPage(string name)
        => name.StartsWith(PagePrefix, StringComparison.Ordinal)
            || name.StartsWith(LegacyPagePrefix, StringComparison.Ordinal);

    /// <summary>The client rejects longer page names. Verified against the real client via
    /// <c>Tools -- runes</c> after an import; adjust here if Riot tightens the limit.</summary>
    private const int MaxNameLength = 30;

    /// <param name="confirmReplace">
    /// Called with a foreign page's name when the page limit forces deleting it; returns whether
    /// the user agreed. Never called for pages DraftPilot created.
    /// </param>
    public static async Task<RuneImportResult> ImportAsync(
        IRunePageClient client,
        RunePage runes,
        string matchupTitle,
        Func<string, bool> confirmReplace,
        CancellationToken ct = default)
    {
        if (runes.PrimaryRuneIds.Count < 4 || runes.SecondaryRuneIds.Count < 2 || runes.ShardIds.Count < 3)
            return new(false, "Runen unvollständig — bitte Build neu laden.");

        var pages = await client.GetRunePagesAsync(ct).ConfigureAwait(false);
        var owned = await client.GetOwnedPageCountAsync(ct).ConfigureAwait(false);

        // A shutdown mid-read surfaces as null too; report it as the abort it is, not as the
        // client misbehaving.
        ct.ThrowIfCancellationRequested();

        // Null means the request failed — very different from "no pages". Counting a failed read
        // as zero would talk the code below into deleting something to make room that exists.
        if (pages is null || owned is null)
            return new(false, "Client hat die Runenseiten nicht verraten — läuft er noch?");

        if (owned <= 0)
            return new(false, "Dieser Account besitzt keine Runenseiten-Slots.");

        // Decide everything before deleting anything, so a "no" really means nothing happened.
        var editable = pages.Where(page => page.IsEditable).ToList();
        var ours = editable.Where(page => IsOurPage(page.Name) && page.CanDelete).ToList();

        // How many pages beyond our own have to go before one slot is free. More than one would
        // mean deleting several of the user's pages for a single import — abort cleanly BEFORE
        // anything is deleted instead of sacrificing one page and failing anyway.
        var needed = editable.Count - ours.Count - owned.Value + 1;
        if (needed > 1)
            return new(false, "Mehr Seiten belegt als Slots vorhanden — bitte im Client aufräumen.");

        LcuRunePage? sacrifice = null;
        if (needed == 1)
        {
            // Something foreign has to make room, and that is the user's call, not ours. Offer
            // the oldest — the page whose loss is most likely already priced in — never the one
            // they are actively using by preference.
            sacrifice = editable
                .Where(page => !IsOurPage(page.Name) && page.CanDelete)
                .OrderBy(page => page.LastModified)
                .FirstOrDefault();

            if (sacrifice is null)
                return new(false, "Alle Seiten belegt und keine löschbar — bitte im Client Platz schaffen.");

            if (!confirmReplace(sacrifice.Name))
                return new(false, "Abgebrochen — keine Seite gelöscht.");
        }

        // Replace our own pages from earlier imports instead of stacking more. All of them: a
        // DirkRunen page and a legacy DraftPilot page can coexist, and a survivor would linger
        // on the account forever.
        foreach (var page in ours)
        {
            ct.ThrowIfCancellationRequested();

            var (deleted, deleteError) = await client.DeleteRunePageAsync(page.Id, ct).ConfigureAwait(false);
            if (!deleted)
                return new(false, $"Alte Seite „{page.Name}“ ließ sich nicht löschen ({deleteError}).");
        }

        if (sacrifice is not null)
        {
            ct.ThrowIfCancellationRequested();

            var (deleted, deleteError) = await client.DeleteRunePageAsync(sacrifice.Id, ct).ConfigureAwait(false);
            if (!deleted)
                return new(false, $"Seite „{sacrifice.Name}“ ließ sich nicht löschen ({deleteError}).");
        }

        var request = new LcuRunePageRequest
        {
            Name = PageName(matchupTitle),
            PrimaryStyleId = runes.PrimaryPathId,
            SubStyleId = runes.SecondaryPathId,
            // Exactly 4 + 2 + 3 — the endpoint's shape. A guide with a fifth primary rune or a
            // fourth shard must not push a ten-perk page at the client.
            SelectedPerkIds =
            [
                .. runes.PrimaryRuneIds.Take(4),
                .. runes.SecondaryRuneIds.Take(2),
                .. runes.ShardIds.Take(3),
            ],
            Current = true,
        };

        var (ok, error) = await client.CreateRunePageAsync(request, ct).ConfigureAwait(false);

        if (ok)
            return new(true, $"Runen übertragen: {runes.PrimaryRunes.FirstOrDefault()} · {runes.PrimaryPath}/{runes.SecondaryPath}");

        // The one failure that loses something: the sacrifice is gone and the replacement was
        // refused. Say so — a message that hides the loss would be worse than the loss.
        return sacrifice is not null
            ? new(false, $"Seite „{sacrifice.Name}“ wurde entfernt, aber der Client hat die neue Seite abgelehnt ({error}).")
            : new(false, $"Client hat die Seite abgelehnt ({error}).");
    }

    private static string PageName(string matchupTitle)
    {
        var name = $"{PagePrefix} · {matchupTitle}";
        if (name.Length <= MaxNameLength)
            return name;

        // Never cut through a surrogate pair; the client rejects names with a lone half.
        var cut = char.IsHighSurrogate(name[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength;
        return name[..cut];
    }
}
