// Port of DraftPilot.Core.Draft.Lane. Numeric like the C# enum and like snapshot.json, so the
// model reads the stored file as it is and the port can be compared line by line.

export const Lane = {
  Unknown: -1,
  Top: 0,
  Jungle: 1,
  Mid: 2,
  Adc: 3,
  Support: 4,
} as const;

export type Lane = (typeof Lane)[keyof typeof Lane];

export const LANE_COUNT = 5;

/** All five real lanes in draft display order; the index equals the value. */
export const ALL_LANES: readonly Lane[] = [Lane.Top, Lane.Jungle, Lane.Mid, Lane.Adc, Lane.Support];

/** Maps the LCU's assignedPosition (and Riot's teamPosition) onto a lane. */
export function laneFromLcu(position: string | null | undefined): Lane {
  switch (position?.toLowerCase()) {
    case 'top':
      return Lane.Top;
    case 'jungle':
      return Lane.Jungle;
    case 'middle':
    case 'mid':
      return Lane.Mid;
    case 'bottom':
    case 'bot':
    case 'adc':
      return Lane.Adc;
    case 'utility':
    case 'support':
      return Lane.Support;
    default:
      return Lane.Unknown;
  }
}

const NAMES = ['top', 'jungle', 'mid', 'adc', 'support'] as const;

/** The camelCase spelling the golden files and the view use. */
export function laneKey(lane: Lane): (typeof NAMES)[number] | 'unknown' {
  return lane >= 0 ? NAMES[lane as number]! : 'unknown';
}

export function laneFromKey(key: string | null | undefined): Lane {
  const index = NAMES.indexOf(key as (typeof NAMES)[number]);
  return index >= 0 ? (index as Lane) : Lane.Unknown;
}

/** The lane as it is called in game, not as an API spells it. */
export function laneDisplay(lane: Lane): string {
  switch (lane) {
    case Lane.Top:
      return 'Toplane';
    case Lane.Jungle:
      return 'Jungle';
    case Lane.Mid:
      return 'Midlane';
    case Lane.Adc:
      return 'Botlane';
    case Lane.Support:
      return 'Support';
    default:
      return '?';
  }
}
