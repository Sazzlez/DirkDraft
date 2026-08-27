using DraftPilot.Core.Lcu;

namespace DraftPilot.Tools;

/// <summary>
/// Read-only look at the account's rune pages: what the client reports, how many pages the account
/// owns, and which page is active. Exists so the endpoint's answer shape can be verified against
/// the real client BEFORE the import ever writes — and so a completed import can be checked with
/// the same command.
/// </summary>
internal static class RunesCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        using var watcher = new LockfileWatcher();
        await watcher.StartAsync(ct);

        if (watcher.Current is not { } credentials)
        {
            Console.Error.WriteLine("Client nicht erreichbar — läuft er?");
            return 1;
        }

        using var client = new LcuClient(credentials);

        var owned = await client.GetOwnedPageCountAsync(ct);
        var pages = await client.GetRunePagesAsync(ct);

        if (pages is null || owned is null)
        {
            Console.Error.WriteLine("Client hat die Runenseiten nicht verraten — falsches Passwort oder Client beim Beenden?");
            return 1;
        }

        var editable = pages.Count(page => page.IsEditable);

        Console.WriteLine($"Seiten-Limit: {owned} · belegt: {editable} (plus {pages.Count - editable} Voreinstellungen)");
        Console.WriteLine();

        foreach (var page in pages)
        {
            var marker = page.Current ? "→" : " ";
            var kind = page.IsEditable ? "eigene" : "Preset";
            var ours = RuneImporter.IsOurPage(page.Name) ? " [DirkRunen]" : "";

            Console.WriteLine($" {marker} #{page.Id}  {page.Name,-32} {kind}{ours}");

            if (page.IsEditable)
            {
                Console.WriteLine($"      Pfade {page.PrimaryStyleId}/{page.SubStyleId} · Perks: {string.Join(", ", page.SelectedPerkIds)}");
            }
        }

        return 0;
    }
}
