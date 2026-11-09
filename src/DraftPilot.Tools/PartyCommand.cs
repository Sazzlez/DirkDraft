using DraftPilot.Core.Lcu;

namespace DraftPilot.Tools;

/// <summary>
/// E1 probe: run once during a champ select with a party (duo or Flex) to see where the client
/// tells who queued together. Reads the session and the lobby, prints only field states and seat
/// numbers (PartyProbe) — no name, no id, nothing written to disk, nothing sent to the client.
/// </summary>
internal static class PartyCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        using var watcher = new LockfileWatcher();
        await watcher.StartAsync(ct);
        if (watcher.Current is not { } credentials)
        {
            Console.WriteLine("Client nicht gefunden — League starten und in den Champ Select gehen.");
            return 1;
        }

        using var client = new LcuClient(credentials);
        var phase = await client.GetGameflowPhaseAsync(ct);
        var session = await client.GetChampSelectSessionRawAsync(ct);
        var lobby = await client.GetLobbyRawAsync(ct);

        foreach (var line in PartyProbe.Report(phase, session, lobby))
            Console.WriteLine(line);
        return 0;
    }
}
