// Numbers and verdicts in the window's language. Every threshold here is 1.5's, with its reason.

import type { Tone } from './view';

const de = (digits: number) =>
  new Intl.NumberFormat('de-DE', { minimumFractionDigits: digits, maximumFractionDigits: digits });

const integer = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 0 });

/** 0,534 → "53,4 %" at the list's precision. */
export function percent(rate: number, decimals: number): string {
  return `${de(decimals).format(rate * 100)} %`;
}

/** Win-rate points, signed: "+1,2" / "−0,4". */
export function points(value: number, decimals = 1): string {
  const text = de(decimals).format(Math.abs(value));
  return value > 0 ? `+${text}` : value < 0 ? `−${text}` : text;
}

export function count(value: number): string {
  return integer.format(value);
}

/** Logit clamped like ScoreModel.Logit: p in [0,01; 0,99], NaN → 0. */
export function logit(p: number): number {
  if (Number.isNaN(p)) return 0;
  const q = Math.min(0.99, Math.max(0.01, p));
  return Math.log(q / (1 - q));
}

/** Below one standard error a resample swaps two rows about a third of the time (ScoreError.TieSigma). */
export const TIE_SIGMA = 1;
/** Ahead by a margin no plausible resample overturns (ScoreError.ClearSigma). */
export const CLEAR_SIGMA = 3;

export type Standing = 'tied' | 'ahead' | 'clear';

/** ScoreError.Standing: the leader against a row, in standard errors of the difference (log-odds). */
export function standing(score: number, error: number, next: number, nextError: number): Standing {
  const gap = logit(score) - logit(next);
  const spread = Math.sqrt(error * error + nextError * nextError);
  if (spread <= 0) return gap > 0 ? 'ahead' : 'tied';
  const sigmas = gap / spread;
  return sigmas >= CLEAR_SIGMA ? 'clear' : sigmas >= TIE_SIGMA ? 'ahead' : 'tied';
}

/** The pick verdict: measured against place 1 of the same list, never against 50 %. */
export function pickVerdict(hasError: boolean, rank: number, s: Standing): { text: string; tone: Tone } {
  if (!hasError) return { text: 'Datenlage zu dünn', tone: 'neutral' };
  if (rank === 1) return { text: 'bester Pick der Liste', tone: 'good' };
  if (s === 'tied') return { text: 'gleichauf mit Platz 1', tone: 'good' };
  if (s === 'ahead') return { text: 'knapp dahinter', tone: 'neutral' };
  return { text: 'deutlich dahinter', tone: 'neutral' };
}

/**
 * The ban verdict. Measured on the first eight bans of five drafts: maximum 4,4, top tenth above
 * 3,6, median 2,6 — so "wichtiger Ban" sits at the top tenth and "sinnvoller Ban" at the middle;
 * below that nothing, rather than eight rows of "optional".
 */
export function banVerdict(value: number): { text: string; tone: Tone } {
  if (value > 3.5) return { text: 'wichtiger Ban', tone: 'good' };
  if (value > 2.5) return { text: 'sinnvoller Ban', tone: 'neutral' };
  return { text: '', tone: 'neutral' };
}

/** A criterion in words, from its contribution in points (TermViewModel thresholds). */
export function termVerdict(value: number | null): string {
  if (value === null) return 'keine Daten';
  if (value >= 2) return 'stark dafür';
  if (value >= 0.6) return 'dafür';
  if (value > -0.6) return 'neutral';
  if (value > -2) return 'dagegen';
  return 'stark dagegen';
}

/** The ban popularity gate in words. */
export function gateVerdict(gate: number): string {
  if (gate >= 0.6) return 'wird oft genommen';
  if (gate >= 0.25) return 'gelegentlich';
  return 'selten';
}

/** Below fifty games one standard error is about seven points: the sample says more than the rate. */
export const MINIMUM_PLAY_FOR_RATE = 50;

export function sampleText(winRate: number, play: number): string {
  return play >= MINIMUM_PLAY_FOR_RATE
    ? `${percent(winRate, 0)} · ${count(play)} Spiele`
    : `dünne Datenlage · ${count(play)} Spiele`;
}

/** "Botlane und Support", "Jungle, Midlane und Support". */
export function joinGerman(parts: string[]): string {
  if (parts.length <= 1) return parts.join('');
  return `${parts.slice(0, -1).join(', ')} und ${parts[parts.length - 1]}`;
}
