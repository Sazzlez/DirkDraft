// The two .NET format strings the 1.5 model writes into its reason texts, reproduced exactly for
// de-DE: "P1"/"P0" (percent) and "N0" (grouped integer).
//
// .NET rounds the exact binary value of the double, half away from zero — 0.5345 is stored as
// 0.53449999…, so P1 gives "53,4 %". toFixed has the same contract (exact value, ties to the larger
// magnitude), while multiplying by 100 first would not, so the decimal point is shifted in the
// string instead.

/** `value.ToString("P{decimals}", de-DE)`: "53,4 %". */
export function netPercent(value: number, decimals: number): string {
  if (Number.isNaN(value)) return 'NaN';
  if (!Number.isFinite(value)) return value > 0 ? '∞ %' : '-∞ %';
  const negative = value < 0 || Object.is(value, -0);
  const fixed = Math.abs(value).toFixed(decimals + 2);
  const [whole, fraction = ''] = fixed.split('.');
  // Shift two digits from the fraction into the integer part.
  const digits = (whole! + fraction).replace(/^0+(?=\d)/, '');
  const intLength = digits.length - decimals;
  const intPart = intLength > 0 ? digits.slice(0, intLength) : '0';
  const fracPart = digits.slice(Math.max(0, intLength)).padStart(decimals, '0');
  const body = groupThousands(intPart.replace(/^0+(?=\d)/, '')) + (decimals > 0 ? ',' + fracPart : '');
  return `${negative && value !== 0 ? '-' : ''}${body} %`;
}

/** `value.ToString("N0", de-DE)`: "1.234". */
export function netN0(value: number): string {
  if (!Number.isFinite(value)) return String(value);
  const negative = value < 0;
  const body = groupThousands(Math.abs(value).toFixed(0));
  return negative && body !== '0' ? `-${body}` : body;
}

function groupThousands(digits: string): string {
  return digits.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
}
