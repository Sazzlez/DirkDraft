// Port of DraftPilot.Core.Draft.QueueKinds (1.5). Names are the camelCase spellings of the C# enum,
// which is also how the golden files write them.

export type QueueKind =
  | 'unknown'
  | 'rankedSolo'
  | 'rankedFlex'
  | 'normalDraft'
  | 'normalBlind'
  | 'quickplay'
  | 'swiftplay'
  | 'clash'
  | 'bots'
  | 'aram'
  | 'aramMayhem'
  | 'arena'
  | 'rotating'
  | 'custom';

export function queueFromId(queueId: number, isCustomGame = false): QueueKind {
  const kind = fromKnownId(queueId);
  if (kind !== 'unknown') return kind;
  return isCustomGame ? 'custom' : 'unknown';
}

function fromKnownId(queueId: number): QueueKind {
  switch (queueId) {
    case 420:
      return 'rankedSolo';
    case 440:
      return 'rankedFlex';
    case 400:
      return 'normalDraft';
    case 430:
      return 'normalBlind';
    case 490:
      return 'quickplay';
    case 480:
      return 'swiftplay';
    case 700:
    case 720:
      return 'clash';
    case 830:
    case 840:
    case 850:
    case 870:
    case 880:
    case 890:
      return 'bots';
    case 450:
      return 'aram';
    case 2400:
    case 2410:
    case 2450:
    case 3270:
    case 3280:
      return 'aramMayhem';
    case 1700:
    case 1710:
    case 1750:
      return 'arena';
    case 900:
    case 1900:
    case 1020:
    case 1300:
    case 1400:
      return 'rotating';
    default:
      return 'unknown';
  }
}

/** Whether the mode has the five Summoner's Rift lanes the model reasons about. */
export const usesLanes = (kind: QueueKind): boolean =>
  kind !== 'aram' && kind !== 'aramMayhem' && kind !== 'arena' && kind !== 'rotating';

export const isAram = (kind: QueueKind): boolean => kind === 'aram' || kind === 'aramMayhem';

export function queueDisplay(kind: QueueKind): string {
  switch (kind) {
    case 'rankedSolo':
      return 'Ranked Solo/Duo';
    case 'rankedFlex':
      return 'Ranked Flex';
    case 'normalDraft':
      return 'Normal Draft';
    case 'normalBlind':
      return 'Normal Blind';
    case 'quickplay':
      return 'Quickplay';
    case 'swiftplay':
      return 'Swiftplay';
    case 'clash':
      return 'Clash';
    case 'bots':
      return 'Co-op vs. AI';
    case 'aram':
      return 'ARAM';
    case 'aramMayhem':
      return 'ARAM Mayhem';
    case 'arena':
      return 'Arena';
    case 'rotating':
      return 'Rotating Gamemode';
    case 'custom':
      return 'Custom Game';
    default:
      return '';
  }
}

/** What the numbers mean in a mode without lanes; empty where they mean what they say. */
export function laneCaveat(kind: QueueKind): string {
  switch (kind) {
    case 'aram':
      return 'ARAM: alle Zahlen sind Lane-Statistiken von Summoner’s Rift. Runen und Items taugen als Richtung, die Reihenfolge der Vorschläge nicht.';
    case 'aramMayhem':
      return 'ARAM Mayhem: alle Zahlen sind Lane-Statistiken von Summoner’s Rift. Runen und Items taugen als Richtung, die Reihenfolge der Vorschläge nicht.';
    case 'arena':
      return 'Arena: 2v2v2v2 ohne Lanes — die Vorschläge beruhen auf Lane-Statistiken und sagen hier nichts.';
    case 'rotating':
      return 'Rotating Gamemode: die Zahlen stammen von Summoner’s Rift und beschreiben ein anderes Spiel als dieses.';
    default:
      return '';
  }
}
