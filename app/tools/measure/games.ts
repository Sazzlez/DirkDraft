// The collector's game lines (games.json.gz, RiotGames.Format in C#) as the measurement tools read
// them: ten champions with their lanes, the winner and the length. Shared by comp.ts and model.ts.

import { readFileSync } from 'node:fs';
import { gunzipSync } from 'node:zlib';
import type { Lane } from '../../src/core/model/lane';

export interface Seat {
  championId: number;
  /** Riot's lane from the game; Unknown (−1) where it gave none. */
  lane: Lane;
}

export interface Game {
  blue: Seat[];
  red: Seat[];
  /** 1 when blue won. */
  blueWins: number;
  /** Full minutes, 0 where unknown. */
  minutes: number;
}

export interface GamesFile {
  tier: string;
  platform: string;
  collectedAtUtc: string;
  patches: { patch: string; games: number[][] }[];
}

/** Each player is champion × 8 + lane + 1; blue's five first, then red's. */
export function decode(line: readonly number[]): Game | null {
  if (line.length < 15) return null;
  const seats = line.slice(0, 10).map((v) => ({ championId: Math.floor(v / 8), lane: ((v % 8) - 1) as Lane }));
  if (seats.some((s) => s.championId <= 0)) return null;
  return { blue: seats.slice(0, 5), red: seats.slice(5), blueWins: line[10] === 1 ? 1 : 0, minutes: Math.max(0, line[11] ?? 0) };
}

export function readGamesFile(path: string): GamesFile {
  return JSON.parse(gunzipSync(readFileSync(path)).toString('utf8')) as GamesFile;
}

/** The games of one patch, decoded; lines that do not split into two sides of five are left out. */
export function gamesOf(file: GamesFile, patch: string): Game[] {
  return (file.patches.find((p) => p.patch === patch)?.games ?? []).map(decode).filter((g): g is Game => g !== null);
}

/** A fixed split for checks: the same games are held out on every run (2 in 10). */
export const isHeldOut = (index: number): boolean => (Math.imul(index + 1, 2654435761) >>> 0) % 10 < 2;
