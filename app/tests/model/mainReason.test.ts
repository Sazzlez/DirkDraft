// The one reason a pick row shows (data/golden.ts mainReason), found in the live test of 07.11.2026.

import { describe, expect, it } from 'vitest';
import { mainReason, type GoldenItem } from '../../src/data/golden';

const term = (kind: string, points: number) => ({ kind, logOdds: points / 25, points, isGate: false, gate: 0, label: kind });
const item = (reasons: GoldenItem['reasons'], breakdown: GoldenItem['breakdown']): GoldenItem => ({
  championId: 202,
  name: 'Jhin',
  score: 0.54,
  uncertainty: 0.02,
  uncertaintyPoints: 0.5,
  reasons,
  breakdown,
});

describe('the reason a row shows', () => {
  it('names the lane strength when it leads and has no reason of its own', () => {
    const jhin = item([{ text: 'ist Ranged', tone: 'pro', hint: null }], [term('laneStrength', 1.2), term('composition', 0.4)]);
    expect(mainReason(jhin, 'adc')).toBe('51,2 % WR auf Botlane');
  });

  it('keeps the first pro reason when another term leads or the lane already has a reason', () => {
    const comp = item([{ text: 'bringt fehlenden AD-Schaden', tone: 'pro', hint: null }], [term('laneStrength', 0.3), term('composition', 2)]);
    expect(mainReason(comp, 'top')).toBe('bringt fehlenden AD-Schaden');
    const lane = item([{ text: '53,4 % WR auf Support', tone: 'pro', hint: null }], [term('laneStrength', 3)]);
    expect(mainReason(lane, 'support')).toBe('53,4 % WR auf Support');
  });

  it('leaves ARAM and ban rows alone', () => {
    const aram = item([{ text: '55,0 % WR in ARAM aus 400 Games', tone: 'neutral', hint: null }], [term('laneStrength', 2)]);
    expect(mainReason(aram, null)).toBe('55,0 % WR in ARAM aus 400 Games');
  });
});
