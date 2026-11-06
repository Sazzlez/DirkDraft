// Port of DraftPilot.Core.Data.PatchVersion (1.5). Data Dragon writes "16.18.1", OP.GG "16.18" and
// the client "16.18.8175716+branch…"; the first two numbers are all they agree on, so comparing them
// means comparing that prefix and nothing else.

const INT32_MAX = 2147483647;

const isAsciiDigit = (character: string | undefined): boolean =>
  character !== undefined && character >= '0' && character <= '9';

/**
 * The "16.18" of any of those spellings, or '' when the value does not start with two numbers.
 * Empty means "do not know", never "0.0": a wrong comparison would be worse than none.
 */
export function patchLine(value: string | null | undefined): string {
  if (value === null || value === undefined || value.trim() === '') return '';

  const text = value.trim();
  let dots = 0;
  let end = 0;
  while (end < text.length) {
    const character = text[end]!;
    if (character === '.') {
      if (++dots === 2) break;
    } else if (!isAsciiDigit(character)) {
      break;
    }
    end++;
  }

  const line = text.slice(0, end);
  // A lone major ("16") is not a patch line, and neither is anything that stops on a dot.
  return dots >= 1 && line.length > 0 && isAsciiDigit(line[line.length - 1]) ? line : '';
}

/**
 * How many patches `newer` is ahead of `older`, or 0 when either is unreadable or the data is not
 * behind. Riot restarts the minor number each year; a season has about 24 patches, which is what a
 * major step counts as — exact enough for an error bar.
 */
export function patchesBetween(older: string | null | undefined, newer: string | null | undefined): number {
  const from = parts(patchLine(older));
  const to = parts(patchLine(newer));
  if (!from || !to) return 0;
  return Math.max(0, (to.major - from.major) * 24 + (to.minor - from.minor));
}

/**
 * Whether two patch strings describe the same patch. An unknown on either side counts as agreement:
 * the comparison exists to warn about a real difference, not about a string nobody could read.
 */
export function samePatchLine(left: string | null | undefined, right: string | null | undefined): boolean {
  const first = patchLine(left);
  const second = patchLine(right);
  return first.length === 0 || second.length === 0 || first === second;
}

/** int.TryParse on both halves: an empty or out-of-range half makes the line unreadable. */
function parts(line: string): { major: number; minor: number } | null {
  const split = line.split('.');
  if (split.length !== 2) return null;
  const major = toInt32(split[0]!);
  const minor = toInt32(split[1]!);
  return major === null || minor === null ? null : { major, minor };
}

function toInt32(text: string): number | null {
  if (!/^\d+$/.test(text)) return null;
  const value = Number(text);
  return value <= INT32_MAX ? value : null;
}
