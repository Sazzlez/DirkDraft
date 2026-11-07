// A plan from DirkDraft's own build counts (core/riotBuilds) as the build area shows it.

import { sampleText } from '../core/format';
import { laneKey } from '../core/model/lane';
import { SHARD_NAMES, type PlanOption, type RiotBuildPlan } from '../core/riotBuilds';
import { extendSkills } from '../core/skillplan';
import { LANE_NAMES, type BuildView, type ChampionRef, type ItemOption, type Lane } from '../core/view';
import { itemName } from './assets';

const viewLane = (lane: RiotBuildPlan['lane']): Lane | null => {
  const key = laneKey(lane);
  return key === 'unknown' ? null : key;
};

const option = (o: PlanOption | undefined): ItemOption | null =>
  o ? { items: o.ids.map((id) => ({ id, name: itemName(id) })), stats: sampleText(o.winRate, o.games) } : null;

/** The ARAM build (aram.json), for ARAM and — as its stand-in — Mayhem. */
export function aramBuild(plan: RiotBuildPlan, champion: ChampionRef, mayhem: boolean): BuildView {
  return {
    ...buildFromRiot(plan, champion, null),
    lane: null,
    title: champion.name,
    source: `ARAM-Build · ${sampleText(plan.winRate, plan.games)}`,
    caveat: mayhem ? 'Aus normalem ARAM gezählt — Mayhem-Spiele gibt Riot nicht heraus, Augments verändern den Build.' : null,
  };
}

export function buildFromRiot(plan: RiotBuildPlan, champion: ChampionRef, opponent: ChampionRef | null): BuildView {
  const lane = viewLane(plan.lane);
  const asked = plan.inPlaceOf === null ? null : viewLane(plan.inPlaceOf);
  const runes = plan.runes?.ids;
  const where = lane ? LANE_NAMES[lane] : '';

  return {
    champion,
    lane,
    title: opponent ? `${champion.name} vs ${opponent.name}` : `${champion.name} · ${where}`,
    source: `Unser Build · ${where} · ${sampleText(plan.winRate, plan.games)}`,
    caveat: asked
      ? `Auf ${LANE_NAMES[asked]} noch keine Spiele gezählt — Build von ${where}`
      : opponent
        ? 'Für alle Gegner gezählt, nicht nur gegen diesen'
        : null,
    runes:
      runes && runes.length === 11
        ? {
            keystone: runes[1]!,
            primaryStyle: runes[0]!,
            primary: runes.slice(1, 5),
            secondaryStyle: runes[5]!,
            secondary: runes.slice(6, 8),
            shards: runes.slice(8).map((id) => SHARD_NAMES[id] ?? `#${id}`),
            stats: sampleText(plan.runes!.winRate, plan.runes!.games),
          }
        : null,
    spells: plan.spells?.ids ?? [],
    start: option(plan.starters[0]),
    boots: option(plan.boots[0]),
    core: option(plan.core[0]),
    late: plan.late.flatMap((o) => o.ids.map((id) => ({ id, name: itemName(id) }))),
    support: option(plan.supportOptions[0] ?? plan.supportChoice ?? undefined),
    alsoSupport: plan.supportOptions.slice(1).map((o) => option(o)!),
    alsoStart: plan.starters.slice(1).map((o) => option(o)!),
    alsoBoots: plan.boots.slice(1).map((o) => option(o)!),
    alsoCore: plan.core.slice(1).map((o) => option(o)!),
    skills: extendSkills(plan.skillOrder.split('')).map((s) => ({ level: s.level, key: s.ability, derived: s.isDerived })),
    skillPriority: plan.skillMax.split('').join(' > '),
  };
}
