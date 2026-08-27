using System.Text.Json;
using DraftPilot.Core.Config;

namespace DraftPilot.Core.Draft;

/// <summary>
/// P(lane | seat index within the team). The client appears to list teams in role order, which
/// would make the seat a useful hint — but that is unverified and Riot could shuffle the enemy
/// order deliberately, so the shipped table only nudges and never overrules what the champion
/// itself says about the lanes it plays.
/// </summary>
public sealed class SeatPriors
{
    private readonly double[] _rows;

    private SeatPriors(double[] rows) => _rows = rows;

    /// <summary>No positional hint at all: every seat equally likely on every lane.</summary>
    public static SeatPriors Uniform { get; } = new(
        [.. Enumerable.Repeat(1.0 / Lanes.Count, Lanes.Count * Lanes.Count)]);

    public double this[int seatIndex, Lane lane]
    {
        get
        {
            if (lane == Lane.Unknown || seatIndex < 0 || seatIndex >= Lanes.Count)
                return 1.0 / Lanes.Count;

            return _rows[(seatIndex * Lanes.Count) + (int)lane];
        }
    }

    /// <summary>
    /// Loads the bundled table. Any problem falls back to <see cref="Uniform"/>: a broken prior
    /// should cost accuracy, not correctness.
    /// </summary>
    public static SeatPriors Load(string? path = null)
    {
        path ??= AppPaths.Bundled("pick_order_priors.json");

        try
        {
            if (!File.Exists(path))
                return Uniform;

            using var document = JsonDocument.Parse(File.ReadAllText(path));

            if (!document.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return Uniform;

            var values = new double[Lanes.Count * Lanes.Count];
            var seat = 0;

            foreach (var row in rows.EnumerateArray())
            {
                if (seat >= Lanes.Count || row.ValueKind != JsonValueKind.Array)
                    break;

                var lane = 0;
                var total = 0.0;

                foreach (var cell in row.EnumerateArray())
                {
                    if (lane >= Lanes.Count)
                        break;

                    // Only numbers; a string or null cell in a hand-edited file must fall back to
                    // Uniform, not crash the view-model's field initialiser and with it startup.
                    if (cell.ValueKind != JsonValueKind.Number || !cell.TryGetDouble(out var raw) || double.IsNaN(raw))
                        return Uniform;

                    var value = Math.Max(0, raw);
                    values[(seat * Lanes.Count) + lane] = value;
                    total += value;
                    lane++;
                }

                if (total <= 0)
                    return Uniform;

                // Normalise so a hand-edited file cannot skew one seat against the others.
                for (var i = 0; i < Lanes.Count; i++)
                    values[(seat * Lanes.Count) + i] /= total;

                seat++;
            }

            return seat == Lanes.Count ? new SeatPriors(values) : Uniform;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            return Uniform;
        }
    }
}
