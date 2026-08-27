namespace DraftPilot.Core.Data;

/// <summary>One level in the skill table.</summary>
/// <param name="Level">1-18.</param>
/// <param name="Ability">Q, W, E or R.</param>
/// <param name="IsDerived">
/// True for levels OP.GG did not report (16-18): filled in by rule, not observed in games. The UI
/// sets these apart because derived is not measured.
/// </param>
public sealed record SkillStep(int Level, string Ability, bool IsDerived);

/// <summary>Turns the guide's 15 observed levels into a full 18-level plan.</summary>
public static class SkillPlan
{
    /// <summary>
    /// Validation cap: five points for every ability. Deliberately NOT three for R — champions
    /// like Udyr level R like a basic ability, and a stricter cap threw their entire real-world
    /// order away. The derivation below still treats three R points as the usual full ultimate.
    /// </summary>
    private const int MaxPointsPerAbility = 5;

    /// <summary>
    /// Extends an observed order (15 levels from the guide, up to 18 from other sources) to a full
    /// 18-level plan: R at 16 (the last ult point is never skipped), then whatever abilities still
    /// lack points, in the order they were first levelled. Returns an empty list when the input is
    /// not a usable order — too short, null entries, or anything that is not Q/W/E/R.
    /// </summary>
    public static IReadOnlyList<SkillStep> Extend(IReadOnlyList<string?> order)
    {
        if (order.Count < 15)
            return [];

        var steps = new List<SkillStep>(18);
        var points = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Observed levels are taken as observed, also beyond 15 — re-deriving what was measured
        // would falsely dim it in the table.
        var observed = Math.Min(order.Count, 18);

        for (var i = 0; i < observed; i++)
        {
            // Validate every entry: a cache file is just JSON on disk, and "X" or null must
            // degrade to "no skill table", not to a table that maxes out garbage.
            var ability = order[i]?.ToUpperInvariant();
            if (ability is not ("Q" or "W" or "E" or "R"))
                return [];

            steps.Add(new SkillStep(i + 1, ability, IsDerived: false));
            points[ability] = points.GetValueOrDefault(ability) + 1;

            if (points[ability] > MaxPointsPerAbility)
                return [];
        }

        var level = observed + 1;

        if (level <= 18 && points.GetValueOrDefault("R") < 3)
        {
            steps.Add(new SkillStep(level++, "R", IsDerived: true));
            points["R"] = points.GetValueOrDefault("R") + 1;
        }

        // Whatever still lacks its points, in the order the build first levelled it.
        // (Snapshot first: the loop appends to the very list it would otherwise be iterating.)
        var firstLevelled = steps.Select(step => step.Ability).Distinct().ToList();

        foreach (var ability in firstLevelled)
        {
            if (ability == "R")
                continue;

            while (level <= 18 && points.GetValueOrDefault(ability) < MaxPointsPerAbility)
            {
                steps.Add(new SkillStep(level++, ability, IsDerived: true));
                points[ability] = points.GetValueOrDefault(ability) + 1;
            }
        }

        return steps;
    }
}
