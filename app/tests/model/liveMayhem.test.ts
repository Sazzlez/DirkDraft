// A real Live Client dump from an ARAM Mayhem game (07.11.2026, minute 15), names replaced by
// "Ich" / "Spieler N". Checks that the game moment reads the API as it really answers, not only as
// our hand-made samples assume.

import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { gameModel, type LiveGameData } from '../../src/core/game';
import { MetaLookup, type MetaSnapshot } from '../../src/core/model/metaLookup';

const read = <T>(path: string): T => JSON.parse(readFileSync(new URL(path, import.meta.url), 'utf8')) as T;
const data = read<LiveGameData>('../fixtures/live-mayhem.json');
const meta = new MetaLookup(read<MetaSnapshot>('../golden/snapshot.json'));

describe('Live Client, ARAM Mayhem', () => {
  it('names the mode KIWI', () => {
    expect(data.gameData?.gameMode).toBe('KIWI');
  });

  it('finds the own row and every champion by its Data Dragon key', () => {
    const model = gameModel(data, meta, null, new Map())!;
    expect(model).not.toBeNull();
    expect(model.championId).toBeGreaterThan(0);
    expect(model.gameTime).toBeGreaterThan(900);
    expect(model.level).toBeGreaterThan(10);
    expect(model.enemies).toHaveLength(5);
    for (const enemy of model.enemies) {
      expect(enemy.championId).toBeGreaterThan(0);
      expect(enemy.items.length).toBeGreaterThan(0);
    }
  });

  it('carries no player name into the model', () => {
    const text = JSON.stringify(gameModel(data, meta, null, new Map()));
    expect(text).not.toMatch(/Spieler|Ich|riotId|summonerName/);
  });
});
