using DraftPilot.Core.Lcu;

namespace DraftPilot.Tools;

/// <summary>
/// Prints every event the client pushes, verbatim. Written to answer a question the higher-level
/// output cannot: which event makes the tool decide champion select ended.
/// </summary>
internal static class EventsCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var watcher = new LockfileWatcher();
        await watcher.StartAsync(ct);

        if (watcher.Current is not { } credentials)
        {
            Console.Error.WriteLine("Client nicht gestartet oder Lockfile nicht lesbar.");
            return 1;
        }

        Console.WriteLine($"Port {credentials.Port}. Jede Zeile ist ein Event. Strg+C beendet.");
        Console.WriteLine();

        await using var socket = new LcuEventSocket(credentials);

        socket.ConnectionChanged += connected =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] SOCKET {(connected ? "verbunden" : "getrennt")}");

        socket.EventReceived += lcuEvent =>
        {
            // The payload for a session is thousands of characters; only its head is informative.
            var data = lcuEvent.Data is null
                ? "<null>"
                : lcuEvent.Data.Length <= 90
                    ? lcuEvent.Data
                    : $"{lcuEvent.Data[..90]}… ({lcuEvent.Data.Length} Zeichen)";

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {lcuEvent.EventType,-6} {lcuEvent.Uri}");
            Console.WriteLine($"           {data}");
        };

        await socket.RunAsync(ct);
        watcher.Dispose();
        return 0;
    }
}
