// Port of DraftPilot.Core.Draft.CompAnalyzer (1.5): what a line-up lacks, and how much a candidate
// covers it. Rule-based on purpose — no win rate behind it, which is why its weight is the smallest.

import { DamageType, hasStaticData, isRanged, isTagged, type ChampionEntry, type MetaLookup } from './metaLookup';
import { contra, neutral, pro, type Reason } from './reason';
import { clamp } from './scoreModel';
import { hasHardCc, hasHardEngage, hasPeel, type ChampionTraits, type TraitTable } from './traits';

export type CompIssue =
  | 'noMagicDamage'
  | 'noPhysicalDamage'
  | 'noFrontline'
  | 'noEngage'
  | 'noPeel'
  | 'littleCrowdControl'
  | 'allMelee'
  | 'littleWaveclear'
  | 'mixedTiming';

/** The win condition a line-up is built around (B3). */
export type Plan = 'teamfight' | 'pick' | 'poke' | 'protect' | 'split';

export const PLAN_NAMES: Record<Plan, string> = {
  teamfight: 'Teamfight',
  pick: 'Pick',
  poke: 'Poke',
  protect: 'Protect',
  split: 'Splitpush',
};

export interface CompFinding {
  issue: CompIssue;
  text: string;
  severity: number;
}

export interface CompProfile {
  count: number;
  physicalShare: number;
  magicShare: number;
  frontlineCount: number;
  rangedCount: number;
  maxEngage: number;
  maxPeel: number;
  totalCrowdControl: number;
  lateScalingCount: number;
  /** Champions at their strongest early (trait "early"). */
  earlyScalingCount: number;
  /** Champions with strong waveclear (trait 2 and up). */
  strongWaveclearCount: number;
  traitsKnown: number;
  findings: CompFinding[];
  /** The plan the line-up is clearly built around, or null (B3; a hint, no weight). */
  plan: Plan | null;
}

export const EMPTY_PROFILE: CompProfile = Object.freeze({
  count: 0,
  physicalShare: 0,
  magicShare: 0,
  frontlineCount: 0,
  rangedCount: 0,
  maxEngage: 0,
  maxPeel: 0,
  totalCrowdControl: 0,
  lateScalingCount: 0,
  earlyScalingCount: 0,
  strongWaveclearCount: 0,
  traitsKnown: 0,
  findings: [],
  plan: null,
});

export const traitCoverage = (p: CompProfile): number => (p.count === 0 ? 0 : p.traitsKnown / p.count);
export const hasDamageMix = (p: CompProfile): boolean => p.physicalShare + p.magicShare > 0;
export const hasIssue = (p: CompProfile, issue: CompIssue): boolean => p.findings.some((f) => f.issue === issue);
export const severityOf = (p: CompProfile, issue: CompIssue): number =>
  p.findings.find((f) => f.issue === issue)?.severity ?? 0;

const DAMAGE_IMBALANCE = 0.2;
const FRONTLINE_DEFENSE = 6;
const HARD_ENGAGE = 2;
const STRONG_WAVECLEAR = 2;
/** A plan needs this much weight in total and this much lead over the next one. */
const PLAN_MINIMUM = 3;
const PLAN_LEAD = 1;
const MINIMUM_ENEMIES_TO_JUDGE = 2;
const MINIMUM_FOR_DAMAGE_RULES = 3;
const MINIMUM_FOR_ROLE_RULES = 3;

export class CompAnalyzer {
  constructor(
    private readonly meta: MetaLookup,
    private readonly traits: TraitTable,
  ) {}

  analyze(championIds: readonly number[]): CompProfile {
    const ids = [...new Set(championIds.filter((id) => id !== 0))];
    if (ids.length === 0) return EMPTY_PROFILE;

    // How much of the side's damage each champion carries, where the file has it for every one of
    // them (own data, 2.1): a Leona brings far less AP than a Syndra. Otherwise one head, one share.
    const amounts = ids.map((id) => damageAmount(this.meta.champion(id)));
    const byAmount = amounts.every((amount) => amount !== null);

    let physical = 0;
    let magic = 0;
    let damageTotal = 0;
    let damageKnown = 0;
    let frontline = 0;
    let ranged = 0;
    let maxEngage = 0;
    let maxPeel = 0;
    let totalCc = 0;
    let lateScaling = 0;
    let earlyScaling = 0;
    let strongWaveclear = 0;
    let traitsKnown = 0;
    let staticKnown = 0;
    const planWeight: Record<Plan, number> = { teamfight: 0, pick: 0, poke: 0, protect: 0, split: 0 };

    ids.forEach((id, index) => {
      const champion = this.meta.champion(id);
      const amount = amounts[index];
      if (byAmount && amount) {
        physical += amount.physical;
        magic += amount.magic;
        damageTotal += amount.physical + amount.magic;
        damageKnown++;
      } else {
        switch (champion?.damage) {
          case DamageType.Physical:
            physical += 1;
            damageTotal += 1;
            damageKnown++;
            break;
          case DamageType.Magic:
            magic += 1;
            damageTotal += 1;
            damageKnown++;
            break;
          case DamageType.Mixed:
            physical += 0.5;
            magic += 0.5;
            damageTotal += 1;
            damageKnown++;
            break;
        }
      }

      if (champion && hasStaticData(champion)) {
        staticKnown++;
        if (isTagged(champion, 'Tank') || champion.defense >= FRONTLINE_DEFENSE) frontline++;
        if (isRanged(champion)) ranged++;
      }

      const trait = this.traits.for(champion?.key);
      if (!trait.isKnown) return;
      traitsKnown++;
      maxEngage = Math.max(maxEngage, trait.engage);
      maxPeel = Math.max(maxPeel, trait.peel);
      totalCc += trait.cc;
      if (trait.scaling === 'late') lateScaling++;
      if (trait.scaling === 'early') earlyScaling++;
      const share = planShare(trait, champion);
      for (const plan of Object.keys(planWeight) as Plan[]) planWeight[plan] += share[plan];
      if (trait.waveclear >= STRONG_WAVECLEAR) strongWaveclear++;
    });

    const physicalShare = damageTotal > 0 ? physical / damageTotal : 0;
    const magicShare = damageTotal > 0 ? magic / damageTotal : 0;
    const findings = evaluate(damageKnown, physicalShare, magicShare, frontline, ranged, maxEngage, maxPeel, totalCc, traitsKnown, staticKnown, strongWaveclear, earlyScaling, lateScaling);

    return {
      count: ids.length,
      physicalShare,
      magicShare,
      frontlineCount: frontline,
      rangedCount: ranged,
      maxEngage,
      maxPeel,
      totalCrowdControl: totalCc,
      lateScalingCount: lateScaling,
      earlyScalingCount: earlyScaling,
      strongWaveclearCount: strongWaveclear,
      traitsKnown,
      findings,
      plan: traitsKnown >= MINIMUM_FOR_ROLE_RULES ? clearPlan(planWeight) : null,
    };
  }

  /** How much the candidate covers the gaps, -1..+1; null where no rule could fire either way. */
  fit(candidateId: number, profile: CompProfile, enemies: CompProfile, reasons: Reason[]): number | null {
    const champion = this.meta.champion(candidateId);
    if (!champion || (profile.count === 0 && enemies.count < MINIMUM_ENEMIES_TO_JUDGE)) return null;

    const trait = this.traits.for(champion.key);
    if (champion.damage === DamageType.Unknown && !hasStaticData(champion) && !trait.isKnown) return null;

    const isMagicish = champion.damage === DamageType.Magic || champion.damage === DamageType.Mixed;
    const isPhysicalish = champion.damage === DamageType.Physical || champion.damage === DamageType.Mixed;
    let score = 0;

    if (hasIssue(profile, 'noMagicDamage') && isMagicish) {
      score += 0.5 * severityOf(profile, 'noMagicDamage');
      reasons.push(
        pro(
          'bringt fehlenden AP-Schaden',
          'Dein Team macht fast nur AD-Schaden. Dagegen reicht dem Gegner Armor — AP-Schaden zwingt ihn, sich gegen beides zu wappnen.',
        ),
      );
    }

    if (hasIssue(profile, 'noPhysicalDamage') && isPhysicalish) {
      score += 0.5 * severityOf(profile, 'noPhysicalDamage');
      reasons.push(
        pro(
          'bringt fehlenden AD-Schaden',
          'Dein Team macht fast nur AP-Schaden. Dagegen reicht dem Gegner MR — AD-Schaden zwingt ihn, sich gegen beides zu wappnen.',
        ),
      );
    }

    if (hasStaticData(champion)) {
      if (hasIssue(profile, 'noFrontline') && (isTagged(champion, 'Tank') || champion.defense >= FRONTLINE_DEFENSE)) {
        score += 0.4;
        reasons.push(
          pro(
            'Frontline',
            'In deinem Team ist noch niemand, der im Teamfight vorne stehen und Schaden abfangen kann (Frontline) — ohne so einen Champ trifft alles direkt die Carrys.',
          ),
        );
      }

      if (hasIssue(profile, 'allMelee') && isRanged(champion)) {
        score += 0.25;
        reasons.push(
          pro(
            'ist Ranged',
            'Dein Team besteht bisher nur aus Melees. Ein Champ mit Range kann Schaden machen, ohne selbst ins Melee zu müssen.',
          ),
        );
      }
    }

    if (trait.isKnown) {
      if (hasIssue(profile, 'noEngage') && hasHardEngage(trait)) {
        score += 0.45;
        reasons.push(
          pro(
            'kann engagen',
            'In deinem Team kann bisher niemand einen Teamfight von sich aus starten (Engage). Dann bestimmt immer der Gegner, wann gekämpft wird.',
          ),
        );
      }

      if (hasIssue(profile, 'noPeel') && hasPeel(trait)) {
        score += 0.25;
        reasons.push(
          pro(
            'Peel',
            'Niemand in deinem Team kann Gegner von den eigenen Carrys wegdrängen (Peel). Ein Assassine kommt so ungestört durch.',
          ),
        );
      }

      if (hasIssue(profile, 'littleCrowdControl') && hasHardCc(trait)) {
        score += 0.3;
        reasons.push(
          pro(
            'bringt Hard CC',
            'Deinem Team fehlen Effekte, die Gegner bewegungsunfähig machen — betäuben, hochwerfen, festhalten (CC). Ohne die entkommt jeder Gegner.',
          ),
        );
      }

      // The smallest of the gap rules: a hint by judgement only, like the rest of this term (2.1).
      if (hasIssue(profile, 'littleWaveclear') && trait.waveclear >= STRONG_WAVECLEAR) {
        score += 0.2;
        reasons.push(
          pro(
            'guter Waveclear',
            'Niemand in deinem Team räumt Minion-Wellen schnell. Ohne Waveclear drückt der Gegner mit seinen Wellen auf eure Türme, und ihr kommt schwer von der Lane weg zu Drachen und Baron.',
          ),
        );
      }

      // Smaller still: the early/late traits are coarse (most champions sit on "mid").
      const majority = timingMajority(profile);
      if (majority && trait.scaling === majority) {
        score += 0.15;
        reasons.push(
          pro(
            majority === 'late' ? 'passt zum Late Game eures Teams' : 'passt zum Early Game eures Teams',
            `In eurem Team stehen schon Champions, die früh, und solche, die spät am stärksten sind. Dieser Pick passt zur Mehrheit (${majority === 'late' ? 'Late Game' : 'Early Game'}), so will das Team in derselben Phase gewinnen. Die Einordnung früh/spät ist grob — die meisten Champions stehen auf „mitte“ — und noch nicht gemessen.`,
          ),
        );
      }
    }

    if (profile.physicalShare >= 0.75 && champion.damage === DamageType.Physical) {
      score -= 0.2;
      reasons.push(
        contra(
          'noch mehr AD-Schaden',
          'Dein Team macht schon fast nur AD-Schaden. Ein weiterer solcher Champ macht es dem Gegner leicht: Er kauft Armor und ist gegen alles gewappnet.',
        ),
      );
    }

    if (profile.magicShare >= 0.75 && champion.damage === DamageType.Magic) {
      score -= 0.2;
      reasons.push(
        contra(
          'noch mehr AP-Schaden',
          'Dein Team macht schon fast nur AP-Schaden. Ein weiterer solcher Champ macht es dem Gegner leicht: Er kauft MR und ist gegen alles gewappnet.',
        ),
      );
    }

    // Fits the plan the team is built around: a hint only, it adds nothing to the score (B3).
    if (trait.isKnown && profile.plan && planShare(trait, champion)[profile.plan] >= 2) {
      reasons.push(
        neutral(
          `passt zu eurem ${PLAN_NAMES[profile.plan]}-Plan`,
          `Euer Team ist bisher auf ${PLAN_NAMES[profile.plan]} gebaut — ${PLAN_WANTS[profile.plan]}. Dieser Pick verstärkt das. ` +
            'Zählt nicht in die Prozentzahl: der Plan ist eine grobe Einordnung aus den Champion-Merkmalen und noch nicht gemessen.',
        ),
      );
    }

    // What the pick does about THEIR line-up; shares the term's cap.
    if (hasStaticData(champion)) {
      if (hasIssue(enemies, 'noFrontline') && isTagged(champion, 'Assassin')) {
        score += 0.3;
        reasons.push(
          pro(
            'ihre Backline steht frei',
            'Im gegnerischen Team steht bisher niemand vorne, der Schaden abfangen kann. Ein Champ, der gezielt auf die hinteren Reihen geht, kommt dort ohne Umweg hin.',
          ),
        );
      }

      if (enemies.frontlineCount >= 2 && isTagged(champion, 'Assassin')) {
        score -= 0.2;
        reasons.push(
          contra(
            'kommt schwer an ihre Carrys',
            'Der Gegner hat schon zwei Champs, die vorne stehen und Schaden aushalten. Sie stehen genau zwischen diesem Pick und den Zielen, für die er gebaut ist.',
          ),
        );
      }

      if (hasIssue(enemies, 'allMelee') && isRanged(champion)) {
        score += 0.25;
        reasons.push(
          pro(
            'Range gegen ein Melee-Team',
            'Der Gegner besteht bisher nur aus Melees. Wer auf Distanz Schaden macht, zwingt sie, den Weg zu ihm erst zurückzulegen.',
          ),
        );
      }
    }

    if (trait.isKnown) {
      if (enemies.maxEngage >= HARD_ENGAGE && hasPeel(trait)) {
        score += 0.3;
        reasons.push(
          pro(
            'Disengage',
            'Der Gegner hat einen Champ, der einen Teamfight von sich aus starten kann. Dieser Pick kann die Getroffenen wieder herausholen (Disengage) — sonst entscheidet ihr Anspiel den Kampf.',
          ),
        );
      }

      if (
        enemies.traitsKnown >= MINIMUM_FOR_ROLE_RULES &&
        enemies.lateScalingCount * 2 >= enemies.traitsKnown &&
        trait.scaling === 'early'
      ) {
        score += 0.25;
        reasons.push(
          pro(
            'Earlygame gegen ein Scaling-Team',
            'Mindestens die Hälfte der aufgedeckten Gegner wird erst im späten Spiel gefährlich. Dieser Champ ist umgekehrt früh am stärksten — er kann das Spiel entscheiden, bevor ihre Kurve greift.',
          ),
        );
      }
    }

    return clamp(score, -1, 1);
  }
}

function evaluate(
  damageKnown: number,
  physicalShare: number,
  magicShare: number,
  frontline: number,
  ranged: number,
  maxEngage: number,
  maxPeel: number,
  totalCc: number,
  traitsKnown: number,
  staticKnown: number,
  strongWaveclear: number,
  earlyScaling: number,
  lateScaling: number,
): CompFinding[] {
  const findings: CompFinding[] = [];

  if (damageKnown >= MINIMUM_FOR_DAMAGE_RULES) {
    if (magicShare < DAMAGE_IMBALANCE)
      findings.push({ issue: 'noMagicDamage', text: 'AP-Schaden fehlt', severity: severity(DAMAGE_IMBALANCE - magicShare, DAMAGE_IMBALANCE) });
    if (physicalShare < DAMAGE_IMBALANCE)
      findings.push({ issue: 'noPhysicalDamage', text: 'AD-Schaden fehlt', severity: severity(DAMAGE_IMBALANCE - physicalShare, DAMAGE_IMBALANCE) });
  }

  // Gated on champions with static data, not head count: without Data Dragon these fire everywhere.
  if (staticKnown >= MINIMUM_FOR_ROLE_RULES && frontline === 0)
    findings.push({ issue: 'noFrontline', text: 'kein Frontline', severity: 0.8 });
  if (staticKnown >= 4 && ranged === 0) findings.push({ issue: 'allMelee', text: 'nur Melee', severity: 0.6 });

  if (traitsKnown < MINIMUM_FOR_ROLE_RULES) return findings;

  if (maxEngage === 0) findings.push({ issue: 'noEngage', text: 'kein Engage', severity: 0.9 });
  if (maxPeel === 0 && traitsKnown >= 4) findings.push({ issue: 'noPeel', text: 'kein Peel', severity: 0.5 });
  // Roughly one piece of hard CC per two champions is the floor for a workable team fight.
  if (totalCc * 2 < traitsKnown) findings.push({ issue: 'littleCrowdControl', text: 'wenig CC', severity: 0.7 });
  // From four champions on, as peel: with three, the mid laner who usually brings it is often still open.
  if (strongWaveclear === 0 && traitsKnown >= 4) findings.push({ issue: 'littleWaveclear', text: 'wenig Waveclear', severity: 0.5 });
  // Early and late at once, three or more between them: the team wants to win in two phases.
  if (earlyScaling >= 1 && lateScaling >= 1 && earlyScaling + lateScaling >= 3)
    findings.push({ issue: 'mixedTiming', text: 'gemischtes Timing', severity: 0.3 });

  return findings;
}

/** What a team with this plan wants, as half a sentence: "Ihr wollt …". */
export const PLAN_WANTS: Record<Plan, string> = {
  teamfight: 'den großen Kampf fünf gegen fünf um Objectives',
  pick: 'einzelne Gegner abfangen, bevor es zum Kampf kommt',
  poke: 'aus der Distanz Schaden machen und Türme belagern, bevor gekämpft wird',
  protect: 'den eigenen Carry schützen, bis er Kämpfe allein entscheidet',
  split: 'über eine Seitenlane Druck machen und den großen Kampf meiden',
};

/**
 * How much one champion adds to each plan: engage 2 is a teamfight starter (multi-target CC on
 * top), an enchanter's peel is protect, the 2.1 traits poke, split and pick count as they are.
 */
function planShare(trait: ChampionTraits, champion: ChampionEntry | null): Record<Plan, number> {
  const enchanter = !!champion && isTagged(champion, 'Support') && !isTagged(champion, 'Tank') && trait.engage < HARD_ENGAGE;
  return {
    teamfight: (trait.engage >= HARD_ENGAGE ? 2 : 0) + (trait.cc >= 3 ? 0.5 : 0),
    pick: trait.pick,
    poke: trait.poke,
    protect: enchanter && trait.peel >= 2 ? 2 : 0,
    split: trait.split,
  };
}

/** The plan with clearly the most weight: enough of it, and ahead of the next by a margin. */
function clearPlan(weight: Record<Plan, number>): Plan | null {
  const ranked = (Object.entries(weight) as [Plan, number][]).sort((a, b) => b[1] - a[1]);
  const [best, second] = ranked;
  return best![1] >= PLAN_MINIMUM && best![1] - second![1] >= PLAN_LEAD ? best![0] : null;
}

/** With mixed timing, the side most of the team is on; null without the finding or on a tie. */
export function timingMajority(p: CompProfile): 'early' | 'late' | null {
  if (!hasIssue(p, 'mixedTiming') || p.earlyScalingCount === p.lateScalingCount) return null;
  return p.earlyScalingCount > p.lateScalingCount ? 'early' : 'late';
}

/** Physical and magic damage per game, when the file has both and they are not nothing. */
function damageAmount(champion: ChampionEntry | null | undefined): { physical: number; magic: number } | null {
  const physical = champion?.physicalDamage;
  const magic = champion?.magicDamage;
  if (typeof physical !== 'number' || typeof magic !== 'number') return null;
  if (!Number.isFinite(physical) || !Number.isFinite(magic) || physical < 0 || magic < 0 || physical + magic <= 0) return null;
  return { physical, magic };
}

const severity = (excess: number, scale: number): number => clamp(excess / Math.max(scale, 0.0001), 0, 1);
