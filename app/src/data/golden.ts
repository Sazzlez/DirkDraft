// Turns one frame of the golden reference (the 1.5 model's own output, see tests/golden/README.md)
// into a ViewModel. M1 renders every moment from real 1.5 results this way; from M2 on the ported
// core produces the same ViewModel directly, and this adapter stays as the yardstick.

import {
  banVerdict,
  count,
  gateVerdict,
  joinGerman,
  percent,
  pickVerdict,
  points,
  sampleText,
  standing,
  termVerdict,
} from '../core/format';
import { momentOf, type DraftFacts } from '../core/moment';
import { extendSkills } from '../core/skillplan';
import {
  LANES,
  LANE_NAMES,
  type AramView,
  type BoardView,
  type BuildView,
  type ChampionRef,
  type CompView,
  type DraftChanceView,
  type HoverView,
  type ItemOption,
  type Lane,
  type MatchupView,
  type Moment,
  type OpenRolesView,
  type PhaseStep,
  type PickTileView,
  type RecommendationView,
  type SlotView,
  type TermView,
  type Tone,
  type VerdictLine,
} from '../core/view';
import { assets, itemName } from './assets';

export interface GoldenSlot {
  cellId: number;
  index: number;
  isAlly: boolean;
  lockedChampionId: number;
  hoverChampionId: number;
  assignedLane: string;
}

export interface GoldenItem {
  championId: number;
  name: string;
  score: number;
  uncertainty: number;
  uncertaintyPoints: number;
  reasons: { text: string; tone: string; hint: string | null }[];
  breakdown: { kind: string; logOdds: number | null; points: number; isGate: boolean; gate: number; label: string }[];
}

export interface GoldenSet {
  cellId: number;
  lane: string;
  action: string;
  tied: number;
  decimals: number;
  items: GoldenItem[];
}

export interface GoldenProfile {
  count: number;
  physicalShare: number;
  magicShare: number;
  frontlineCount: number;
  rangedCount: number;
  maxEngage: number;
  maxPeel: number;
  totalCrowdControl: number;
  lateScalingCount: number;
  traitsKnown: number;
  findings: { issue: string; text: string; severity: number }[];
}

export interface GoldenFrame {
  state: {
    isActive: boolean;
    phase: string;
    queue: string;
    usesLanes: boolean;
    localCellId: number;
    allies: GoldenSlot[];
    enemies: GoldenSlot[];
    allyBans: number[];
    enemyBans: number[];
    unavailable: number[];
    turn: { cellId: number; isAlly: boolean; isLocalPlayer: boolean; action: string } | null;
    bench: number[];
    rerollsRemaining: number;
  };
  target: { cellId: number; action: string; isFollowingTurn: boolean } | null;
  predictions: {
    enemies: { cellId: number; championId: number; lane: string; confidence: number; isUncertain: boolean; isManual: boolean }[];
    allies: { cellId: number; championId: number; lane: string; confidence: number; isUncertain: boolean; isManual: boolean }[];
  };
  picks: GoldenSet | null;
  bans: GoldenSet | null;
  hover: { championId: number; rank: number; candidates: number; lane: string; leader: GoldenItem | null; item: GoldenItem; decimals: number } | null;
  comp: { allies: GoldenProfile; enemies: GoldenProfile };
  balance: { allyWinRate: number; ratedChampions: number; contestedLanes: number; isComparable: boolean; hasData: boolean };
  laneMatchups: { lane: string; allyId: number; enemyId: number; winRate: number | null; play: number; isInferred: boolean; measured: number | string | null }[];
  aramBench: { own: number; bench: number[]; rerolls: number; tied: number; decimals: number; items: GoldenItem[] } | null;
  /** Live frames with a known pool only (core/engine). */
  poolPicks?: GoldenSet | null;
}

export interface SnapshotChampion {
  id: number;
  key: string;
  name: string;
}

const MODES: Record<string, string> = {
  rankedSolo: 'Ranked Solo/Duo',
  rankedFlex: 'Ranked Flex',
  aram: 'ARAM',
  aramMayhem: 'ARAM Mayhem*',
  normalDraft: 'Normal Draft',
  swiftplay: 'Swiftplay',
  custom: 'Custom',
  unknown: 'Ranked Solo/Duo',
};

export function toLane(value: string | null | undefined): Lane | null {
  return value && (LANES as readonly string[]).includes(value) ? (value as Lane) : null;
}

export class GoldenAdapter {
  private readonly champions: Map<number, SnapshotChampion>;

  constructor(champions: SnapshotChampion[]) {
    this.champions = new Map(champions.map((c) => [c.id, c]));
  }

  champion(id: number): ChampionRef | null {
    if (!id) return null;
    const own = this.champions.get(id);
    const localised = assets()?.champions.get(id);
    return {
      id,
      key: localised?.key ?? own?.key ?? String(id),
      name: localised?.name ?? own?.name ?? `#${id}`,
    };
  }

  facts(frame: GoldenFrame): DraftFacts {
    const s = frame.state;
    const me = s.allies.find((slot) => slot.cellId === s.localCellId);
    return {
      isActive: s.isActive,
      phase: (s.phase as DraftFacts['phase']) ?? 'unknown',
      usesLanes: s.usesLanes,
      isAram: s.queue === 'aram' || s.queue === 'aramMayhem',
      myLocked: (me?.lockedChampionId ?? 0) !== 0,
      turn: s.turn ? { isAlly: s.turn.isAlly, isLocalPlayer: s.turn.isLocalPlayer, action: s.turn.action as 'pick' | 'ban' } : null,
      targetAction: (frame.target?.action as 'pick' | 'ban' | undefined) ?? null,
    };
  }

  moment(frame: GoldenFrame): Moment {
    return momentOf(this.facts(frame), 'ChampSelect');
  }

  board(frame: GoldenFrame, seconds: number | null): BoardView {
    const s = frame.state;
    const predicted = new Map([...frame.predictions.allies, ...frame.predictions.enemies].map((p) => [p.cellId, p]));
    const unavailable = new Set(s.unavailable);

    const slot = (g: GoldenSlot): SlotView => {
      const isMe = g.cellId === s.localCellId;
      const prediction = predicted.get(g.cellId);
      const lane = toLane(g.assignedLane) ?? (s.usesLanes ? toLane(prediction?.lane) : null);
      const shown = g.lockedChampionId || g.hoverChampionId;
      const allyHoverBlocked = g.isAlly && !g.lockedChampionId && g.hoverChampionId && unavailable.has(g.hoverChampionId);
      return {
        cellId: g.cellId,
        side: g.isAlly ? 'ally' : 'enemy',
        label: isMe ? 'Du' : g.isAlly ? `Teammate ${g.index + 1}` : `Gegner ${g.index + 1}`,
        champion: this.champion(shown),
        hovering: !g.lockedChampionId && !!g.hoverChampionId,
        lane: s.usesLanes ? lane : null,
        laneConfidence: !g.isAlly && prediction && shown ? prediction.confidence : null,
        laneUncertain: !g.isAlly && !!shown && !!prediction?.isUncertain,
        isMe,
        isTarget: frame.target?.cellId === g.cellId,
        warning: allyHoverBlocked ? 'gebannt' : null,
      };
    };

    const turn = s.turn;
    const label = (cellId: number) => {
      const all = [...s.allies, ...s.enemies];
      const g = all.find((x) => x.cellId === cellId);
      if (!g) return '';
      return g.cellId === s.localCellId ? 'Du' : g.isAlly ? `Teammate ${g.index + 1}` : `Gegner ${g.index + 1}`;
    };

    const turnText = !turn
      ? s.phase === 'finalization'
        ? 'Finalisierung'
        : s.phase === 'planning'
          ? 'Planung'
          : 'Warten'
      : turn.isLocalPlayer
        ? turn.action === 'ban'
          ? 'Du bannst'
          : 'Du pickst'
        : `${label(turn.cellId)} ${turn.action === 'ban' ? 'bannt' : 'pickt'}`;

    return {
      allies: s.allies.map(slot),
      enemies: s.enemies.map(slot),
      allyBans: s.allyBans.map((id) => this.champion(id)).filter((c): c is ChampionRef => !!c),
      enemyBans: s.enemyBans.map((id) => this.champion(id)).filter((c): c is ChampionRef => !!c),
      seconds,
      turnText,
      myTurn: !!turn?.isLocalPlayer,
      // ARAM has no bans and no pick order to walk through — only the timer.
      steps: s.usesLanes ? steps(frame) : [],
      mode: MODES[s.queue] ?? 'Ranked Solo/Duo',
      modeNote: s.queue === 'aramMayhem' ? 'Mayhem nutzt die ARAM-Zahlen derselben Karte.' : null,
      usesLanes: s.usesLanes,
      bench: s.queue === 'aram' || s.queue === 'aramMayhem'
        ? s.bench.map((id) => this.champion(id)).filter((c): c is ChampionRef => !!c)
        : null,
    };
  }

  recommendation(item: GoldenItem, rank: number, leader: GoldenItem, decimals: number, isBan: boolean, tied: boolean): RecommendationView {
    const champion = this.champion(item.championId) ?? { id: item.championId, key: '', name: item.name };
    const verdict = isBan
      ? banVerdict(item.score)
      : pickVerdict(item.uncertainty > 0, rank, standing(leader.score, leader.uncertainty, item.score, item.uncertainty));
    const reasons = item.reasons.map((r) => ({ text: r.text, tone: toneOf(r.tone) }));
    const main = item.reasons.find((r) => r.tone === 'pro') ?? item.reasons[0];
    return {
      champion,
      value: isBan ? `${points(item.score)} Pkt` : percent(item.score, decimals),
      score: item.score,
      errorPoints: item.uncertaintyPoints,
      verdict: verdict.text,
      tone: verdict.tone,
      reason: main?.text ?? (isBan ? '' : verdict.text),
      reasons,
      terms: item.breakdown.map(term),
      tied,
      unavailable: false,
      inPool: true,
    };
  }

  pickTile(set: GoldenSet, frame: GoldenFrame, title: string): PickTileView {
    const isBan = set.action === 'ban';
    const pool = isBan ? null : (frame.poolPicks ?? null);
    const inPool = new Set(pool?.items.map((i) => i.championId) ?? []);
    const rows = (s: GoldenSet) =>
      s.items.map((item, i) => {
        const row = this.recommendation(item, i + 1, s.items[0]!, s.decimals, isBan, i < s.tied);
        return pool ? { ...row, inPool: inPool.has(item.championId) } : row;
      });
    return {
      kind: isBan ? 'bans' : 'picks',
      title,
      lane: toLane(set.lane),
      tied: set.tied,
      items: rows(set),
      hover: isBan ? null : this.hover(frame),
      footnote: isBan
        ? 'Bann-Wert: so viel Winrate nimmt der Bann dem Gegner, mal der Chance, dass er den Champion überhaupt pickt.'
        : 'Balken: Beitrag jedes Kriteriums in Prozentpunkten über 50 %. Rot zieht ab.',
      // Without a known pool the switch would offer an empty list; it is hidden instead.
      poolAvailable: !isBan && (pool?.items.length ?? 0) > 0,
      poolItems: pool && pool.items.length > 0 ? rows(pool) : null,
      poolTied: pool?.tied ?? 0,
    };
  }

  hover(frame: GoldenFrame): HoverView | null {
    const h = frame.hover;
    if (!h) return null;
    const champion = this.champion(h.championId)!;
    const leader = h.leader ?? h.item;
    const s = standing(leader.score, leader.uncertainty, h.item.score, h.item.uncertainty);
    const verdict = h.rank === 1 ? 'bester Pick' : s === 'tied' ? 'gleichauf mit Platz 1' : s === 'ahead' ? 'knapp dahinter' : 'deutlich dahinter';
    return {
      champion,
      value: h.item.uncertainty > 0 ? percent(h.item.score, h.decimals) : null,
      place: h.rank > 0 ? `Platz ${h.rank} von ${h.candidates} · ${verdict}` : 'nicht in der Liste für diese Lane',
      tone: h.rank === 1 || s === 'tied' ? 'good' : 'neutral',
      note: null,
    };
  }

  openRoles(frame: GoldenFrame): OpenRolesView {
    const taken = new Set(
      frame.predictions.enemies.filter((p) => p.championId !== 0).map((p) => toLane(p.lane)).filter((l): l is Lane => !!l),
    );
    const lanes = LANES.filter((lane) => !taken.has(lane));
    return {
      lanes,
      text: lanes.length === 0 ? 'Der Gegner hat alle Rollen besetzt.' : `Dem Gegner fehlt noch ${joinGerman(lanes.map((l) => LANE_NAMES[l]))}.`,
    };
  }

  chance(frame: GoldenFrame): DraftChanceView {
    const b = frame.balance;
    if (!b.hasData) {
      return { ally: null, allyText: '—', enemyText: '—', verdict: 'Noch nicht vergleichbar', note: 'Erst wenn beide Seiten etwas aufgedeckt haben.' };
    }
    const gap = Math.abs(b.allyWinRate - 0.5) * 100;
    return {
      ally: b.allyWinRate,
      allyText: percent(b.allyWinRate, 1),
      enemyText: percent(1 - b.allyWinRate, 1),
      verdict: gap < 1 ? 'Ausgeglichen' : b.allyWinRate > 0.5 ? 'Ihr liegt vorn' : 'Der Gegner liegt vorn',
      note: 'Unter einem Punkt Abstand ist das Rauschen.',
    };
  }

  comp(frame: GoldenFrame): CompView {
    const a = frame.comp.allies;
    const e = frame.comp.enemies;
    const damage = (p: GoldenProfile) => (p.count === 0 ? null : { physical: p.physicalShare, magic: p.magicShare });
    const finding = a.findings[0] ?? null;
    return {
      rows: [
        { label: 'Engage', ally: a.maxEngage, enemy: e.maxEngage, max: 3 },
        { label: 'Frontline', ally: a.frontlineCount, enemy: e.frontlineCount, max: 5 },
        { label: 'Peel', ally: a.maxPeel, enemy: e.maxPeel, max: 3 },
        { label: 'Spätes Spiel', ally: a.lateScalingCount, enemy: e.lateScalingCount, max: 5 },
      ],
      allyDamage: damage(a),
      enemyDamage: damage(e),
      verdict: finding ? `Euch: ${finding.text}` : e.findings[0] ? `Gegner: ${e.findings[0].text}` : 'Keine Lücke auf beiden Seiten',
      verdictTone: finding ? 'warn' : 'good',
    };
  }

  matchup(frame: GoldenFrame, me: ChampionRef, lane: Lane): MatchupView {
    const duel = frame.laneMatchups.find((m) => m.lane === lane);
    const opponent = duel ? this.champion(duel.enemyId) : null;
    const rate = duel?.winRate ?? null;
    const measured = typeof duel?.measured === 'number' ? duel.measured : null;
    return {
      me,
      opponent,
      lane,
      value: rate === null ? null : percent(rate, 1),
      sample: duel && duel.play > 0 ? `gemessen ${measured === null ? '—' : percent(measured, 1)} in ${count(duel.play)} Spielen` : 'keine Statistik für dieses Matchup',
      verdict: rate === null ? '—' : Math.abs(rate - 0.5) < 0.01 ? 'ausgeglichen' : rate > 0.5 ? 'läuft für dich' : 'läuft gegen dich',
      tone: rate === null ? 'neutral' : rate > 0.51 ? 'good' : rate < 0.49 ? 'bad' : 'neutral',
    };
  }

  verdictLines(frame: GoldenFrame): VerdictLine[] {
    const a = frame.comp.allies;
    const e = frame.comp.enemies;
    const lines: VerdictLine[] = [];
    if (a.maxEngage >= 2 && e.maxEngage < 2) lines.push({ icon: 'engage', tone: 'good', title: 'Ihr habt mehr Engage', detail: 'Ihr könnt Kämpfe eröffnen, sie kaum.' });
    else if (e.maxEngage >= 2 && a.maxEngage < 2) lines.push({ icon: 'engage', tone: 'warn', title: 'Sie haben mehr Engage', detail: 'Peel und Abstand halten zählen.' });
    if (e.lateScalingCount > a.lateScalingCount) lines.push({ icon: 'scaling', tone: 'warn', title: 'Sie skalieren besser', detail: 'Früh Druck machen, Objectives nehmen.' });
    else if (a.lateScalingCount > e.lateScalingCount) lines.push({ icon: 'scaling', tone: 'good', title: 'Ihr skaliert besser', detail: 'Ruhig spielen, spät gewinnt ihr Kämpfe.' });
    if (e.physicalShare >= 0.65) lines.push({ icon: 'damage', tone: 'info', title: 'Viel AD beim Gegner', detail: 'Armor zuerst lohnt sich.' });
    else if (e.magicShare >= 0.65) lines.push({ icon: 'damage', tone: 'info', title: 'Viel AP beim Gegner', detail: 'MR zuerst lohnt sich.' });
    return lines.slice(0, 3);
  }

  aram(frame: GoldenFrame): AramView | null {
    const bench = frame.aramBench;
    if (!bench || bench.items.length === 0) return null;
    const leader = bench.items[0]!;
    return {
      rows: bench.items.map((item, i) => ({
        ...this.recommendation(item, i + 1, leader, bench.decimals, false, i < bench.tied),
        own: item.championId === bench.own,
      })),
      rerolls: bench.rerolls,
      tied: bench.tied,
      note: 'Die ARAM-Winrate kommt auf ganze Prozent gerundet; die Rundung steckt im Fehlerbalken.',
    };
  }
}

/** The phase timeline from what has happened so far (ban and lock counts). */
function steps(frame: GoldenFrame): PhaseStep[] {
  const s = frame.state;
  const bans = s.allyBans.length + s.enemyBans.length;
  const locks = [...s.allies, ...s.enemies].filter((slot) => slot.lockedChampionId).length;
  const labels = ['Bann 1', 'Pick 1–3', 'Bann 2', 'Pick 4–5', 'Finalisierung'];
  let now: number;
  if (s.phase === 'planning') now = -1;
  else if (s.phase === 'finalization') now = 4;
  else if (bans < 6 && locks === 0) now = 0;
  else if (locks < 6) now = 1;
  else if (bans < 10) now = 2;
  else now = 3;
  return labels.map((label, i) => ({ label, state: i < now ? 'done' : i === now ? 'now' : 'next' }));
}

function toneOf(tone: string): Tone {
  return tone === 'pro' ? 'good' : tone === 'contra' ? 'bad' : 'neutral';
}

const TERM_KINDS: Record<string, TermView['kind']> = {
  laneStrength: 'lane',
  laneMatchup: 'matchup',
  enemyTeam: 'enemies',
  synergy: 'synergy',
  composition: 'comp',
};

function term(t: GoldenItem['breakdown'][number]): TermView {
  const value = t.logOdds === null ? null : t.isGate ? null : t.points;
  return {
    label: t.label,
    points: value,
    gate: t.isGate ? t.gate : null,
    verdict: t.isGate ? gateVerdict(t.gate) : termVerdict(value),
    hint: '',
    kind: TERM_KINDS[t.kind] ?? 'ban',
  };
}

// --- builds -------------------------------------------------------------------------------------

interface PlanSet {
  items: string[];
  itemIds: number[];
  winRate: number;
  play: number;
}

export interface GoldenPlan {
  championId: number;
  championName: string;
  opponentId: number;
  opponentName: string;
  lane: string;
  patch: string;
  runes: {
    primaryPathId: number;
    primaryRuneIds: number[];
    secondaryPathId: number;
    secondaryRuneIds: number[];
    shards: string[];
    winRate: number;
    play: number;
  } | null;
  starters: PlanSet[];
  boots: PlanSet[];
  coreItems: PlanSet[];
  lateItems: PlanSet[];
  summonerSpells: PlanSet[];
  skillOrder: string[];
  skillPriority: string;
}

const option = (set: PlanSet | undefined): ItemOption | null =>
  set ? { items: set.itemIds.map((id) => ({ id, name: itemName(id) })), stats: sampleText(set.winRate, set.play) } : null;

/** Alternatives from 20 games on, the rate from 50 (GameBuildViewModel.MinimumAlternativePlay). */
const alternatives = (sets: PlanSet[]): ItemOption[] =>
  sets.slice(1).filter((set) => set.play >= 20).map((set) => option(set)!);

export function buildFromPlan(plan: GoldenPlan, champion: ChampionRef, opponent: ChampionRef | null): BuildView {
  const lane = toLane(plan.lane);
  const runes = plan.runes;
  return {
    champion,
    lane,
    title: opponent ? `${champion.name} vs ${opponent.name}` : `${champion.name} · ${lane ? LANE_NAMES[lane] : ''}`,
    source: opponent ? 'Matchup-Build' : 'Bester Build der Lane',
    caveat: opponent ? null : 'Kein Matchup bekannt — bester Build der Lane',
    runes: runes
      ? {
          keystone: runes.primaryRuneIds[0] ?? 0,
          primaryStyle: runes.primaryPathId,
          primary: runes.primaryRuneIds,
          secondaryStyle: runes.secondaryPathId,
          secondary: runes.secondaryRuneIds,
          shards: runes.shards,
          stats: sampleText(runes.winRate, runes.play),
        }
      : null,
    spells: plan.summonerSpells[0]?.itemIds ?? [],
    start: option(plan.starters[0]),
    boots: option(plan.boots[0]),
    core: option(plan.coreItems[0]),
    late: plan.lateItems.flatMap((set) => set.itemIds.map((id) => ({ id, name: itemName(id) }))),
    alsoStart: alternatives(plan.starters),
    alsoBoots: alternatives(plan.boots),
    alsoCore: alternatives(plan.coreItems),
    skills: extendSkills(plan.skillOrder).map((s) => ({ level: s.level, key: s.ability, derived: s.isDerived })),
    skillPriority: plan.skillPriority,
  };
}
