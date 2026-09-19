using System.Text;
using DraftPilot.Core.Draft;

namespace DraftPilot.Tools;

/// <summary>
/// Console rendering of a draft snapshot. This is the milestone-1 deliverable: proof that the
/// client is read correctly, before any of it goes into a window.
/// </summary>
internal static class DraftStatePrinter
{
    public static void Print(DraftSnapshot snapshot)
    {
        var (state, target, status, _) = snapshot;

        if (!state.IsActive)
        {
            Console.WriteLine($"[{Timestamp()}] kein Champ Select  ({Describe(status)})");
            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"[{Timestamp()}] {state.Phase}  ({Describe(status)})");
        text.AppendLine($"  Am Zug: {DescribeTurn(state)}");
        text.AppendLine($"  Ziel:   {DescribeTarget(target)}");
        text.AppendLine($"  Bans    wir: {Join(state.AllyBans)}   sie: {Join(state.EnemyBans)}");

        text.AppendLine("  Team:");
        foreach (var slot in state.Allies)
            text.AppendLine($"    {DescribeSlot(slot, state, isAlly: true)}");

        text.AppendLine("  Gegner:");
        foreach (var slot in state.Enemies)
            text.AppendLine($"    {DescribeSlot(slot, state, isAlly: false)}");

        Console.Write(text.ToString());
    }

    private static string DescribeSlot(DraftSlot slot, DraftState state, bool isAlly)
    {
        var label = isAlly
            ? slot.CellId == state.LocalCellId ? "DU          " : $"Teammate {slot.Index + 1}"
            : $"Gegner {slot.Index + 1}    ";

        var champion = slot.LockedChampionId != 0
            ? $"locked {slot.LockedChampionId}"
            : slot.HoverChampionId != 0
                ? $"hover  {slot.HoverChampionId}"
                : "-";

        var lane = slot.AssignedLane == Lane.Unknown ? "?" : slot.AssignedLane.Display();
        var onClock = state.Turn?.CellId == slot.CellId ? " <<<" : string.Empty;

        return $"{label} cell={slot.CellId,-2} {lane,-8} {champion}{onClock}";
    }

    private static string DescribeTurn(DraftState state)
    {
        if (state.Turn is not { } turn)
            return "niemand";

        var who = turn.IsLocalPlayer ? "DU" : turn.IsAlly ? $"Teammate cell={turn.CellId}" : $"Gegner cell={turn.CellId}";
        return $"{who} / {turn.Action}";
    }

    private static string DescribeTarget(RecommendationTarget? target)
    {
        if (target is null)
            return "-";

        var mode = target.IsFollowingTurn ? "folgt Zug" : "Ausblick";
        var lane = target.Slot.AssignedLane == Lane.Unknown ? "?" : target.Slot.AssignedLane.Display();
        return $"cell={target.Slot.CellId} {lane} / {target.Action} ({mode})";
    }

    private static string Describe(DraftPilot.Core.Lcu.ClientStatus status)
    {
        if (!status.LeagueFound)
            return status.Detail ?? "League nicht gefunden";

        if (!status.ClientRunning)
            return status.Detail ?? "Client aus";

        return status.SocketConnected ? status.Detail ?? "verbunden" : status.Detail ?? "Socket getrennt";
    }

    private static string Join(IReadOnlyList<int> ids) => ids.Count == 0 ? "-" : string.Join(",", ids);

    private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss");
}
