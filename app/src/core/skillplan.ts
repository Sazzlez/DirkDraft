// Port of DraftPilot.Core.Data.SkillPlan (1.5). Golden-tested against builds/skills.json.

export type Ability = 'Q' | 'W' | 'E' | 'R';

/** One level of the skill table; derived levels were filled in by rule, not observed in games. */
export interface SkillStep {
  level: number;
  ability: Ability;
  isDerived: boolean;
}

/**
 * Five points for every ability — deliberately not three for R: champions like Udyr level R like a
 * basic ability, and a stricter cap threw their whole real-world order away.
 */
const MAX_POINTS_PER_ABILITY = 5;

/**
 * Extends an observed order (15 levels, up to 18) to a full plan: R at the next level while R has
 * fewer than three points, then whatever still lacks points in the order first levelled. Empty
 * when the input is not a usable order — too short, null entries, anything not Q/W/E/R.
 */
export function extendSkills(order: readonly (string | null | undefined)[]): SkillStep[] {
  if (order.length < 15) return [];

  const steps: SkillStep[] = [];
  const points = new Map<Ability, number>();
  const observed = Math.min(order.length, 18);

  for (let i = 0; i < observed; i++) {
    const ability = order[i]?.toUpperCase();
    if (ability !== 'Q' && ability !== 'W' && ability !== 'E' && ability !== 'R') return [];

    steps.push({ level: i + 1, ability, isDerived: false });
    points.set(ability, (points.get(ability) ?? 0) + 1);
    if ((points.get(ability) ?? 0) > MAX_POINTS_PER_ABILITY) return [];
  }

  let level = observed + 1;

  if (level <= 18 && (points.get('R') ?? 0) < 3) {
    steps.push({ level: level++, ability: 'R', isDerived: true });
    points.set('R', (points.get('R') ?? 0) + 1);
  }

  const firstLevelled = [...new Set(steps.map((step) => step.ability))];
  for (const ability of firstLevelled) {
    if (ability === 'R') continue;
    while (level <= 18 && (points.get(ability) ?? 0) < MAX_POINTS_PER_ABILITY) {
      steps.push({ level: level++, ability, isDerived: true });
      points.set(ability, (points.get(ability) ?? 0) + 1);
    }
  }

  return steps;
}
