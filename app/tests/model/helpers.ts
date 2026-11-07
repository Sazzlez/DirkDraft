// TS counterparts of the xUnit helpers in tests/DraftPilot.Core.Tests (TestMeta, MetaBuilder,
// SessionBuilder), so the ported cases read like the originals.

import type { DraftSlot } from '../../src/core/model/draftState';
import { Lane } from '../../src/core/model/lane';
import { DamageType, emptySnapshot, MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';

/** `new DraftSlot(cellId, index, isAlly, locked, hover, lane)`. */
export function slot(
  cellId: number,
  index: number,
  isAlly: boolean,
  lockedChampionId: number,
  hoverChampionId: number,
  assignedLane: Lane,
): DraftSlot {
  return { cellId, index, isAlly, lockedChampionId, hoverChampionId, assignedLane };
}

export const TestMeta = {
  Aatrox: 266,
  LeeSin: 64,
  Ahri: 103,
  Ashe: 22,
  Thresh: 412,
  Jax: 24,
  Elise: 60,
  Syndra: 134,
  Lucian: 236,
  Nautilus: 111,
  Sylas: 517,

  standardCast(): MetaLookup {
    const snapshot: MetaSnapshot = { ...emptySnapshot(), patch: 'test' };
    const add = TestMeta.addChampion;
    add(snapshot, TestMeta.Aatrox, 'Aatrox', [Lane.Top, 0.92], [Lane.Mid, 0.08]);
    add(snapshot, TestMeta.LeeSin, 'LeeSin', [Lane.Jungle, 0.97]);
    add(snapshot, TestMeta.Ahri, 'Ahri', [Lane.Mid, 0.95], [Lane.Support, 0.05]);
    add(snapshot, TestMeta.Ashe, 'Ashe', [Lane.Adc, 0.94], [Lane.Support, 0.06]);
    add(snapshot, TestMeta.Thresh, 'Thresh', [Lane.Support, 0.99]);
    add(snapshot, TestMeta.Jax, 'Jax', [Lane.Top, 0.86], [Lane.Jungle, 0.14]);
    add(snapshot, TestMeta.Elise, 'Elise', [Lane.Jungle, 0.93], [Lane.Top, 0.07]);
    add(snapshot, TestMeta.Syndra, 'Syndra', [Lane.Mid, 0.96]);
    add(snapshot, TestMeta.Lucian, 'Lucian', [Lane.Adc, 0.8], [Lane.Mid, 0.2]);
    add(snapshot, TestMeta.Nautilus, 'Nautilus', [Lane.Support, 0.9], [Lane.Jungle, 0.1]);
    // A genuine flex pick, to check that ambiguity shows up as low confidence.
    add(snapshot, TestMeta.Sylas, 'Sylas', [Lane.Mid, 0.45], [Lane.Jungle, 0.3], [Lane.Top, 0.25]);
    return new MetaLookup(snapshot);
  },

  addChampion(snapshot: MetaSnapshot, id: number, name: string, ...lanes: [Lane, number][]): void {
    snapshot.champions.push({ id, key: name, name, damage: DamageType.Unknown, tags: [], attackRange: 0, defense: 0 });
    for (const [lane, roleRate] of lanes) {
      snapshot.laneStats.push({ championId: id, lane, roleRate, winRate: 0.5, play: 500, pickRate: 0, banRate: 0, tier: 0 });
    }
  },

  enemySlots(...championIds: number[]): DraftSlot[] {
    return championIds.map((id, i) => slot(i + 5, i, false, id, 0, Lane.Unknown));
  },
};

export class MetaBuilder {
  private readonly snapshot: MetaSnapshot = { ...emptySnapshot(), patch: 'test' };

  champion(
    id: number,
    name: string,
    options: { damage?: DamageType; tags?: string[]; attackRange?: number; defense?: number } = {},
  ): this {
    this.snapshot.champions.push({
      id,
      key: name,
      name,
      damage: options.damage ?? DamageType.Unknown,
      tags: [...(options.tags ?? [])],
      attackRange: options.attackRange ?? 0,
      defense: options.defense ?? 0,
    });
    return this;
  }

  inLane(
    championId: number,
    lane: Lane,
    options: { roleRate?: number; winRate?: number; play?: number; tier?: number; pickRate?: number; banRate?: number } = {},
  ): this {
    this.snapshot.laneStats.push({
      championId,
      lane,
      roleRate: options.roleRate ?? 0.9,
      winRate: options.winRate ?? 0.5,
      play: options.play ?? 1000,
      tier: options.tier ?? -1,
      pickRate: options.pickRate ?? 0,
      banRate: options.banRate ?? 0,
    });
    return this;
  }

  matchup(championId: number, opponentId: number, lane: Lane, winRate: number, play = 1000): this {
    this.snapshot.matchups.push({ championId, opponentId, lane, winRate, play });
    return this;
  }

  synergy(
    championId: number,
    partnerId: number,
    winRate: number,
    options: { play?: number; tier?: number; lane?: Lane; partnerLane?: Lane } = {},
  ): this {
    this.snapshot.synergies.push({
      championId,
      partnerId,
      winRate,
      play: options.play ?? 1000,
      tier: options.tier ?? -1,
      lane: options.lane ?? Lane.Top,
      partnerLane: options.partnerLane ?? Lane.Top,
    });
    return this;
  }

  synergyBaseline(logOdds: number): this {
    this.snapshot.synergyBaseline = logOdds;
    return this;
  }

  matchupBaseline(logOdds: number): this {
    this.snapshot.matchupBaseline = logOdds;
    return this;
  }

  /** The raw snapshot, for tests that edit it before building. */
  snapshotRef(): MetaSnapshot {
    return this.snapshot;
  }

  build(): MetaLookup {
    return new MetaLookup(this.snapshot);
  }
}

interface Player {
  cellId: number;
  championId: number;
  championPickIntent: number;
  assignedPosition: string | null;
  team: number;
}

interface Action {
  id: number;
  actorCellId: number;
  championId: number;
  type: string;
  completed: boolean;
  isInProgress: boolean;
  isAllyAction: boolean;
}

/** Builds an LCU champ-select session object as the client sends it. */
export class SessionBuilder {
  private static readonly allyPositions = ['top', 'jungle', 'middle', 'bottom', 'utility'];

  private readonly session = {
    localPlayerCellId: 2,
    queueId: 0,
    isCustomGame: false,
    myTeam: [] as Player[],
    theirTeam: [] as Player[],
    actions: [] as Action[][],
    bans: { myTeamBans: [] as number[], theirTeamBans: [] as number[], numBans: 0 },
    timer: { phase: 'BAN_PICK' as string | null },
    isSpectating: false,
    benchEnabled: false,
    benchChampions: [] as { championId: number; isPriority: boolean }[],
    rerollsRemaining: 0,
  };

  private readonly pending: Action[] = [];

  constructor() {
    for (let i = 0; i < 5; i++) {
      this.session.myTeam.push({ cellId: i, championId: 0, championPickIntent: 0, assignedPosition: SessionBuilder.allyPositions[i]!, team: 1 });
      this.session.theirTeam.push({ cellId: i + 5, championId: 0, championPickIntent: 0, assignedPosition: '', team: 2 });
    }
  }

  localPlayer(cellId: number): this {
    this.session.localPlayerCellId = cellId;
    return this;
  }

  queue(queueId: number): this {
    this.session.queueId = queueId;
    return this;
  }

  phase(phase: string | null): this {
    this.session.timer.phase = phase;
    return this;
  }

  locked(cellId: number, championId: number): this {
    this.slot(cellId).championId = championId;
    return this;
  }

  hovering(cellId: number, championId: number): this {
    this.slot(cellId).championPickIntent = championId;
    return this;
  }

  allyLane(cellId: number, position: string | null): this {
    this.slot(cellId).assignedPosition = position;
    return this;
  }

  summaryBans(ally: number[], enemy: number[]): this {
    this.session.bans.myTeamBans.push(...ally);
    this.session.bans.theirTeamBans.push(...enemy);
    return this;
  }

  completedBan(actorCellId: number, championId: number): this {
    return this.action(actorCellId, championId, 'ban', true, false);
  }

  onClock(actorCellId: number, type: string): this {
    return this.action(actorCellId, 0, type, false, true);
  }

  action(actorCellId: number, championId: number, type: string, completed: boolean, inProgress: boolean): this {
    this.pending.push({
      id: this.pending.length + 1,
      actorCellId,
      championId,
      type,
      completed,
      isInProgress: inProgress,
      isAllyAction: actorCellId < 5,
    });
    return this;
  }

  bench(...championIds: number[]): this {
    this.session.benchEnabled = true;
    this.session.benchChampions = championIds.map((championId) => ({ championId, isPriority: false }));
    return this;
  }

  spectating(): this {
    this.session.isSpectating = true;
    return this;
  }

  /** A deep copy, so later builder calls do not change a session already handed out. */
  build(): Record<string, unknown> {
    this.session.actions = this.pending.length > 0 ? [[...this.pending]] : [];
    return JSON.parse(JSON.stringify(this.session)) as Record<string, unknown>;
  }

  private slot(cellId: number): Player {
    const player = [...this.session.myTeam, ...this.session.theirTeam].find((p) => p.cellId === cellId);
    if (!player) throw new Error(`no slot ${cellId}`);
    return player;
  }
}
