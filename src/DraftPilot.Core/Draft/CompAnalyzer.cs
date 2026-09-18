using DraftPilot.Core.Data;

namespace DraftPilot.Core.Draft;

public enum CompIssue
{
    NoMagicDamage,
    NoPhysicalDamage,
    NoFrontline,
    NoEngage,
    NoPeel,
    LittleCrowdControl,
    AllMelee,
}

/// <summary>One thing a composition is missing.</summary>
/// <param name="Severity">0 to 1; scales how strongly a candidate that fixes it is rewarded.</param>
public sealed record CompFinding(CompIssue Issue, string Text, double Severity);

/// <summary>
/// What a set of champions adds up to. <see cref="TraitsKnown"/> says how much of it rests on
/// curated data, so the UI can be honest about which checks actually ran — and what the trait
/// numbers are a sum over.
/// </summary>
public sealed record CompProfile(
    int Count,
    double PhysicalShare,
    double MagicShare,
    int FrontlineCount,
    int RangedCount,
    int MaxEngage,
    int MaxPeel,
    int TotalCrowdControl,
    int LateScalingCount,
    int TraitsKnown,
    IReadOnlyList<CompFinding> Findings)
{
    /// <summary>Share of the champions the curated traits cover; 0 when none of them do.</summary>
    public double TraitCoverage => Count == 0 ? 0 : (double)TraitsKnown / Count;

    public static CompProfile Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);

    /// <summary>
    /// At least one champion's damage type is known, so the two shares mean something.
    /// <para>
    /// Without this the pair reads as "0 % physisch · 0 % magisch" — a composition that deals no
    /// damage at all — where the truth is that nothing about its damage has been read yet. The
    /// shares add up to 1 as soon as a single champion is known, so their sum is the test.
    /// </para>
    /// </summary>
    public bool HasDamageMix => PhysicalShare + MagicShare > 0;

    public bool Has(CompIssue issue) => Findings.Any(finding => finding.Issue == issue);

    public double SeverityOf(CompIssue issue)
        => Findings.FirstOrDefault(finding => finding.Issue == issue)?.Severity ?? 0;
}

/// <summary>
/// Rule-based composition analysis. This is the part that keeps working when the statistics are
/// thin: "your team has no way to start a fight" does not need a win rate to be true.
/// <para>
/// Every rule states its own precondition. A rule whose inputs are missing produces nothing at
/// all — silence rather than a guess.
/// </para>
/// </summary>
public sealed class CompAnalyzer(MetaLookup meta, TraitTable traits)
{
    /// <summary>Below this share of one damage type, the composition is one-dimensional.</summary>
    private const double DamageImbalance = 0.2;

    /// <summary>Riot's defensive rating from which a champion counts as a frontline body.</summary>
    private const int FrontlineDefense = 6;

    /// <summary>Rules need a few champions on the board before they say anything useful.</summary>
    private const int MinimumForDamageRules = 3;

    private const int MinimumForRoleRules = 3;

    private readonly MetaLookup _meta = meta;
    private readonly TraitTable _traits = traits;

    public CompProfile Analyze(IReadOnlyList<int> championIds)
    {
        var ids = championIds.Where(id => id != 0).Distinct().ToList();
        if (ids.Count == 0)
            return CompProfile.Empty;

        double physical = 0, magic = 0, damageKnown = 0;
        int frontline = 0, ranged = 0, maxEngage = 0, maxPeel = 0, totalCc = 0, lateScaling = 0, traitsKnown = 0, staticKnown = 0;

        foreach (var id in ids)
        {
            var champion = _meta.Champion(id);

            switch (champion?.Damage)
            {
                case DamageType.Physical:
                    physical += 1;
                    damageKnown++;
                    break;
                case DamageType.Magic:
                    magic += 1;
                    damageKnown++;
                    break;
                case DamageType.Mixed:
                    physical += 0.5;
                    magic += 0.5;
                    damageKnown++;
                    break;
            }

            if (champion is not null && champion.HasStaticData)
            {
                staticKnown++;

                if (champion.IsTagged("Tank") || champion.Defense >= FrontlineDefense)
                    frontline++;

                if (champion.IsRanged)
                    ranged++;
            }

            var trait = _traits.For(champion?.Key);
            if (!trait.IsKnown)
                continue;

            traitsKnown++;
            maxEngage = Math.Max(maxEngage, trait.Engage);
            maxPeel = Math.Max(maxPeel, trait.Peel);
            totalCc += trait.Cc;

            if (trait.Scaling == ScalingCurve.Late)
                lateScaling++;
        }

        var physicalShare = damageKnown > 0 ? physical / damageKnown : 0;
        var magicShare = damageKnown > 0 ? magic / damageKnown : 0;

        var findings = Evaluate(
            damageKnown, physicalShare, magicShare,
            frontline, ranged, maxEngage, maxPeel, totalCc, traitsKnown, staticKnown);

        return new CompProfile(
            ids.Count, physicalShare, magicShare, frontline, ranged,
            maxEngage, maxPeel, totalCc, lateScaling, traitsKnown, findings);
    }

    private static List<CompFinding> Evaluate(
        double damageKnown,
        double physicalShare,
        double magicShare,
        int frontline,
        int ranged,
        int maxEngage,
        int maxPeel,
        int totalCc,
        int traitsKnown,
        int staticKnown)
    {
        var findings = new List<CompFinding>();

        if (damageKnown >= MinimumForDamageRules)
        {
            if (magicShare < DamageImbalance)
                findings.Add(new CompFinding(CompIssue.NoMagicDamage, "AP-Schaden fehlt", Severity(DamageImbalance - magicShare, DamageImbalance)));

            if (physicalShare < DamageImbalance)
                findings.Add(new CompFinding(CompIssue.NoPhysicalDamage, "AD-Schaden fehlt", Severity(DamageImbalance - physicalShare, DamageImbalance)));
        }

        // Gated on champions with actual static data, not on head count: with Data Dragon
        // missing (the update ran without a patch version), frontline and range are zero for
        // EVERYONE, and these two fired on every composition as pure false alarms.
        if (staticKnown >= MinimumForRoleRules && frontline == 0)
            findings.Add(new CompFinding(CompIssue.NoFrontline, "kein Frontline", 0.8));

        if (staticKnown >= 4 && ranged == 0)
            findings.Add(new CompFinding(CompIssue.AllMelee, "nur Nahkampf", 0.6));

        // Trait-driven rules need enough curated champions on the board to mean anything.
        if (traitsKnown < MinimumForRoleRules)
            return findings;

        if (maxEngage == 0)
            findings.Add(new CompFinding(CompIssue.NoEngage, "kein Engage", 0.9));

        if (maxPeel == 0 && traitsKnown >= 4)
            findings.Add(new CompFinding(CompIssue.NoPeel, "kein Peel", 0.5));

        // Roughly one piece of hard CC per two champions is the floor for a workable team fight.
        if (totalCc * 2 < traitsKnown)
            findings.Add(new CompFinding(CompIssue.LittleCrowdControl, "wenig CC", 0.7));

        return findings;
    }

    /// <summary>
    /// How much a candidate would improve the composition, from -1 to about +1, with the reasons
    /// that produced the number. <see langword="null"/> when there is nothing to judge yet — no
    /// team-mate has picked, or the champion is unknown — as opposed to a considered zero.
    /// </summary>
    public double? Fit(int candidateId, CompProfile profile, ICollection<Reason> reasons)
    {
        var champion = _meta.Champion(candidateId);
        if (champion is null || profile.Count == 0)
            return null;

        var trait = _traits.For(champion.Key);

        // No damage type, no static data, no curated traits: not one rule below can fire, in either
        // direction. That is missing data, not a considered zero, and the breakdown says so.
        if (champion.Damage == DamageType.Unknown && !champion.HasStaticData && !trait.IsKnown)
            return null;

        var score = 0.0;

        // Covering a missing damage type is the single most valuable thing a late pick can do.
        if (profile.Has(CompIssue.NoMagicDamage) && champion.Damage is DamageType.Magic or DamageType.Mixed)
        {
            score += 0.5 * profile.SeverityOf(CompIssue.NoMagicDamage);
            reasons.Add(Reason.Pro(
                "bringt fehlenden magischen Schaden",
                "Dein Team macht fast nur physischen Schaden. Dagegen reicht dem Gegner Rüstung — "
                + "magischer Schaden zwingt ihn, sich gegen beides zu wappnen."));
        }

        if (profile.Has(CompIssue.NoPhysicalDamage) && champion.Damage is DamageType.Physical or DamageType.Mixed)
        {
            score += 0.5 * profile.SeverityOf(CompIssue.NoPhysicalDamage);
            reasons.Add(Reason.Pro(
                "bringt fehlenden physischen Schaden",
                "Dein Team macht fast nur magischen Schaden. Dagegen reicht dem Gegner "
                + "Magieresistenz — physischer Schaden zwingt ihn, sich gegen beides zu wappnen."));
        }

        if (champion.HasStaticData)
        {
            if (profile.Has(CompIssue.NoFrontline) && (champion.IsTagged("Tank") || champion.Defense >= FrontlineDefense))
            {
                score += 0.4;
                reasons.Add(Reason.Pro(
                    "hält vorne Schaden aus",
                    "In deinem Team ist noch niemand, der im Teamfight vorne stehen und Schaden "
                    + "abfangen kann — ohne so einen Champion trifft alles direkt die Carrys."));
            }

            if (profile.Has(CompIssue.AllMelee) && champion.IsRanged)
            {
                score += 0.25;
                reasons.Add(Reason.Pro(
                    "kämpft auf Distanz",
                    "Dein Team besteht bisher nur aus Nahkämpfern. Ein Champion mit Reichweite kann "
                    + "Schaden machen, ohne selbst in den Nahkampf zu müssen."));
            }
        }

        if (trait.IsKnown)
        {
            if (profile.Has(CompIssue.NoEngage) && trait.HasHardEngage)
            {
                score += 0.45;
                reasons.Add(Reason.Pro(
                    "kann Kämpfe eröffnen",
                    "In deinem Team kann bisher niemand einen Teamfight von sich aus starten "
                    + "(Engage). Dann bestimmt immer der Gegner, wann gekämpft wird."));
            }

            if (profile.Has(CompIssue.NoPeel) && trait.HasPeel)
            {
                score += 0.25;
                reasons.Add(Reason.Pro(
                    "schützt deine Carrys",
                    "Niemand in deinem Team kann Gegner von den eigenen Schadensausteilern "
                    + "wegdrängen (Peel). Ein Assassine kommt so ungestört durch."));
            }

            if (profile.Has(CompIssue.LittleCrowdControl) && trait.HasHardCc)
            {
                score += 0.3;
                reasons.Add(Reason.Pro(
                    "kann Gegner festsetzen",
                    "Deinem Team fehlen Effekte, die Gegner bewegungsunfähig machen — betäuben, "
                    + "hochwerfen, festhalten (CC). Ohne die entkommt jeder Gegner."));
            }
        }

        // Deepening an existing imbalance is a real cost, not a neutral choice. (No need to check
        // the NoPhysicalDamage finding here: a share ≥ 0.75 rules it out by definition.)
        if (profile.PhysicalShare >= 0.75 && champion.Damage == DamageType.Physical)
        {
            score -= 0.2;
            reasons.Add(Reason.Contra(
                "noch mehr physischer Schaden",
                "Dein Team macht schon fast nur physischen Schaden. Ein weiterer solcher Champion "
                + "macht es dem Gegner leicht: Er kauft Rüstung und ist gegen alles gewappnet."));
        }

        if (profile.MagicShare >= 0.75 && champion.Damage == DamageType.Magic)
        {
            score -= 0.2;
            reasons.Add(Reason.Contra(
                "noch mehr magischer Schaden",
                "Dein Team macht schon fast nur magischen Schaden. Ein weiterer solcher Champion "
                + "macht es dem Gegner leicht: Er kauft Magieresistenz und ist gegen alles gewappnet."));
        }

        // The documented range is -1..+1 and ScoreModel.CompScale is calibrated for it ("a
        // covered gap ≈ +4 points, never more"). Unclamped, a candidate covering every gap at
        // once stacked up to 2.15 — turning the one term WITHOUT a win-rate basis into the
        // second-largest in the model.
        return Math.Clamp(score, -1, 1);
    }

    private static double Severity(double excess, double scale)
        => Math.Clamp(excess / Math.Max(scale, 0.0001), 0, 1);
}
