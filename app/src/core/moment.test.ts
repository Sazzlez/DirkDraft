import { describe, expect, it } from 'vitest';
import { momentOf, nextArea, type DraftFacts } from './moment';

const draft = (patch: Partial<DraftFacts> = {}): DraftFacts => ({
  isActive: true,
  phase: 'banPick',
  usesLanes: true,
  isAram: false,
  myLocked: false,
  turn: { isAlly: true, isLocalPlayer: true, action: 'pick' },
  targetAction: 'pick',
  ...patch,
});

describe('momentOf', () => {
  it('follows the gameflow before the draft', () => {
    expect(momentOf(draft(), 'InProgress')).toBe('game');
    expect(momentOf(null, 'GameStart')).toBe('loading');
    expect(momentOf(null, 'EndOfGame')).toBe('postgame');
    expect(momentOf(null, 'Lobby')).toBe('lobby');
  });

  it('names every draft moment', () => {
    expect(momentOf(draft({ phase: 'planning' }), 'ChampSelect')).toBe('planning');
    expect(momentOf(draft({ turn: { isAlly: true, isLocalPlayer: true, action: 'ban' } }), null)).toBe('banning');
    expect(momentOf(draft(), null)).toBe('picking');
    expect(momentOf(draft({ turn: { isAlly: true, isLocalPlayer: false, action: 'pick' } }), null)).toBe('teammate');
    expect(momentOf(draft({ myLocked: true }), null)).toBe('locked');
    expect(momentOf(draft({ phase: 'finalization' }), null)).toBe('locked');
    expect(momentOf(draft({ isAram: true, usesLanes: false }), null)).toBe('aramBench');
  });

  it('shows the outlook while the enemy is on the clock', () => {
    const enemyBans = draft({ turn: { isAlly: false, isLocalPlayer: false, action: 'ban' }, targetAction: 'ban' });
    expect(momentOf(enemyBans, null)).toBe('banning');
  });
});

describe('nextArea', () => {
  it('keeps a manual choice until the moment changes', () => {
    expect(nextArea('picking', 'picking', 'build', 'draft')).toEqual({ area: 'build', manual: 'build' });
    expect(nextArea('picking', 'game', 'build', 'game')).toEqual({ area: 'game', manual: null });
  });
});
