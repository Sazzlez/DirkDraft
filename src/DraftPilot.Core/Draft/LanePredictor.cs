using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

/// <summary>What we think one seat is playing.</summary>
/// <param name="CellId">The seat.</param>
/// <param name="ChampionId">0 while the pick is still hidden.</param>
/// <param name="Lane">The lane from the most likely overall assignment.</param>
/// <param name="Confidence">
/// Marginal probability of <paramref name="Lane"/> for this seat: how much of the total likelihood
/// across all assignments puts this seat there.
/// </param>
/// <param name="Probabilities">Marginal per lane, indexed by <see cref="Draft.Lane"/> value.</param>
/// <param name="IsManual">True when the user fixed this seat by hand.</param>
public sealed record LanePrediction(
    long CellId,
    int ChampionId,
    Lane Lane,
    double Confidence,
    double[] Probabilities,
    bool IsManual)
{
    /// <summary>Below this, the UI should tell the user to look for themselves.</summary>
    public const double LowConfidence = 0.6;

    public bool IsUncertain => !IsManual && Confidence < LowConfidence;
}

public sealed class LanePredictionResult
{
    private readonly Dictionary<long, LanePrediction> _byCell;
    private readonly Dictionary<Lane, LanePrediction> _byLane;

    internal LanePredictionResult(IReadOnlyList<LanePrediction> predictions)
    {
        Predictions = predictions;

        // TryAdd, not ToDictionary: a corrupt payload with a duplicated cell id must degrade the
        // prediction, not throw out of a render pass.
        _byCell = [];
        foreach (var prediction in predictions)
            _byCell.TryAdd(prediction.CellId, prediction);

        _byLane = [];

        // The assignment is one-to-one, so a lane maps back to exactly one seat.
        foreach (var prediction in predictions.Where(p => p.Lane != Lane.Unknown))
            _byLane.TryAdd(prediction.Lane, prediction);
    }

    public static LanePredictionResult Empty { get; } = new([]);

    public IReadOnlyList<LanePrediction> Predictions { get; }

    public LanePrediction? ForCell(long cellId)
        => _byCell.TryGetValue(cellId, out var prediction) ? prediction : null;

    /// <summary>The champion we expect on a lane, or 0 if that seat has not revealed a pick.</summary>
    public int ChampionOnLane(Lane lane)
        => _byLane.TryGetValue(lane, out var prediction) ? prediction.ChampionId : 0;
}

/// <summary>
/// Guesses which lane each enemy pick will end up on.
/// <para>
/// Rather than an assignment algorithm that yields one answer, this enumerates all one-to-one
/// mappings of seats onto lanes — at most 120 of them — and weighs each by how plausible it is.
/// That costs microseconds and produces real probabilities as a by-product, which is what the
/// confidence badge and the manual override both need.
/// </para>
/// </summary>
public sealed class LanePredictor(MetaLookup meta, SeatPriors? seatPriors = null)
{
    /// <summary>
    /// Floor for a champion never seen on a lane. Nonzero on purpose: an off-meta pick must stay
    /// possible, otherwise a single unusual champion can leave every assignment at zero likelihood.
    /// </summary>
    private const double OffMetaFloor = 0.01;

    private readonly MetaLookup _meta = meta;
    private readonly SeatPriors _seatPriors = seatPriors ?? SeatPriors.Uniform;

    /// <summary>
    /// Predicts lanes for a set of seats.
    /// </summary>
    /// <param name="slots">The seats to assign, in the order the client lists them.</param>
    /// <param name="manualLanes">Lanes the user fixed by hand; these constrain everything else.</param>
    public LanePredictionResult Predict(IReadOnlyList<DraftSlot> slots, IReadOnlyDictionary<long, Lane>? manualLanes = null)
    {
        if (slots.Count == 0)
            return LanePredictionResult.Empty;

        var seatCount = Math.Min(slots.Count, Lanes.Count);
        var weights = BuildWeights(slots, seatCount, manualLanes);

        // Marginal likelihood mass per (seat, lane), plus the single most likely assignment.
        var marginals = new double[seatCount * Lanes.Count];
        var total = 0.0;
        var bestLikelihood = -1.0;
        var bestAssignment = new int[seatCount];
        var assignment = new int[seatCount];

        Enumerate(0, 0, 1.0);

        // Contradictory hard constraints — two seats forced onto the same lane — zero out every
        // assignment and with it EVERY seat's prediction, including the unambiguous ones.
        // Predicting without the manual constraints is strictly more useful than predicting
        // nothing at all.
        if (total <= 0 && manualLanes is { Count: > 0 })
            return Predict(slots, manualLanes: null);

        var predictions = new List<LanePrediction>(slots.Count);

        for (var seat = 0; seat < slots.Count; seat++)
        {
            var slot = slots[seat];

            if (seat >= seatCount)
            {
                // More than five seats would mean the client changed shape; report honestly.
                predictions.Add(new LanePrediction(slot.CellId, slot.EffectiveChampionId, Lane.Unknown, 0, new double[Lanes.Count], false));
                continue;
            }

            var probabilities = new double[Lanes.Count];
            if (total > 0)
            {
                for (var laneIndex = 0; laneIndex < Lanes.Count; laneIndex++)
                    probabilities[laneIndex] = marginals[(seat * Lanes.Count) + laneIndex] / total;
            }

            var isManual = manualLanes is not null && manualLanes.TryGetValue(slot.CellId, out var fixedLane) && fixedLane != Lane.Unknown;
            var lane = total > 0 ? (Lane)bestAssignment[seat] : Lane.Unknown;
            var confidence = lane == Lane.Unknown ? 0 : probabilities[(int)lane];

            predictions.Add(new LanePrediction(slot.CellId, slot.EffectiveChampionId, lane, confidence, probabilities, isManual));
        }

        return new LanePredictionResult(predictions);

        // Depth-first over lanes not yet taken; `used` is a bitmask of assigned lanes.
        void Enumerate(int depth, int used, double likelihood)
        {
            if (depth == seatCount)
            {
                if (likelihood <= 0)
                    return;

                total += likelihood;

                for (var i = 0; i < seatCount; i++)
                    marginals[(i * Lanes.Count) + assignment[i]] += likelihood;

                if (likelihood > bestLikelihood)
                {
                    bestLikelihood = likelihood;
                    assignment.CopyTo(bestAssignment, 0);
                }

                return;
            }

            for (var candidate = 0; candidate < Lanes.Count; candidate++)
            {
                if ((used & (1 << candidate)) != 0)
                    continue;

                var weight = weights[(depth * Lanes.Count) + candidate];
                if (weight <= 0)
                    continue;

                assignment[depth] = candidate;
                Enumerate(depth + 1, used | (1 << candidate), likelihood * weight);
            }
        }
    }

    /// <summary>
    /// How plausible each seat-lane pairing is on its own, before the one-to-one constraint.
    /// </summary>
    private double[] BuildWeights(IReadOnlyList<DraftSlot> slots, int seatCount, IReadOnlyDictionary<long, Lane>? manualLanes)
    {
        var weights = new double[seatCount * Lanes.Count];

        for (var seat = 0; seat < seatCount; seat++)
        {
            var slot = slots[seat];

            // A manual override is a hard constraint: it removes every other lane for this seat.
            if (manualLanes is not null
                && manualLanes.TryGetValue(slot.CellId, out var fixedLane)
                && fixedLane != Lane.Unknown)
            {
                weights[(seat * Lanes.Count) + (int)fixedLane] = 1;
                continue;
            }

            // The client tells us ally lanes outright; no need to guess those.
            if (slot.AssignedLane != Lane.Unknown)
            {
                weights[(seat * Lanes.Count) + (int)slot.AssignedLane] = 1;
                continue;
            }

            var championId = slot.EffectiveChampionId;

            for (var lane = 0; lane < Lanes.Count; lane++)
            {
                var seatPrior = _seatPriors[seat, (Lane)lane];

                // Nothing revealed yet: only the seat's position says anything.
                if (championId == 0)
                {
                    weights[(seat * Lanes.Count) + lane] = seatPrior;
                    continue;
                }

                var rolePrior = Math.Max(_meta.RolePrior(championId, (Lane)lane), OffMetaFloor);
                weights[(seat * Lanes.Count) + lane] = rolePrior * seatPrior;
            }
        }

        return weights;
    }
}
