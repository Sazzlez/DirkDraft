// Moves the 1.5 reference files onto a deliberate model change (README.md, "Neu in 2.1"): every value
// the engine now computes differently is written into the reference, everything else stays byte for
// byte. The writer formats exactly as the C# tool did (System.Text.Json, indented, default encoder),
// so the file diff shows the changed values and nothing else.

import { ADDED_IN_2_1, compare } from './harness';

const pad = (depth: number): string => '  '.repeat(depth);

/** A double as System.Text.Json writes it: shortest round trip, E notation below 1e-4 and from 1e15. */
export function formatNumber(x: number): string {
  if (Number.isNaN(x)) return '"NaN"';
  if (Object.is(x, -0)) return '-0';
  if (Number.isInteger(x) && Math.abs(x) < 1e15) return String(x);
  const [mantissa, exponentText] = x.toExponential().split('e') as [string, string];
  const exponent = Number(exponentText);
  if (exponent <= -5 || exponent >= 15)
    return `${mantissa}E${exponent < 0 ? '-' : '+'}${String(Math.abs(exponent)).padStart(2, '0')}`;
  return String(x);
}

const SHORT: Record<string, string> = { '\b': '\\b', '\t': '\\t', '\n': '\\n', '\f': '\\f', '\r': '\\r', '"': '\\"', '\\': '\\\\' };

/** The default encoder: everything outside printable ASCII and the HTML-sensitive characters as \uXXXX. */
export function formatString(s: string): string {
  let out = '"';
  for (let i = 0; i < s.length; i++) {
    const ch = s[i]!;
    const code = s.charCodeAt(i);
    if (SHORT[ch]) out += SHORT[ch];
    else if (code < 0x20 || code > 0x7e || '<>&\'+`'.includes(ch)) out += `\\u${code.toString(16).toUpperCase().padStart(4, '0')}`;
    else out += ch;
  }
  return `${out}"`;
}

export function writeJson(value: unknown, depth: number, nl: string): string {
  if (value === null || value === undefined) return 'null';
  if (typeof value === 'boolean') return String(value);
  if (typeof value === 'number') return formatNumber(value);
  if (typeof value === 'string') return formatString(value);
  if (Array.isArray(value))
    return value.length === 0
      ? '[]'
      : `[${nl}${value.map((item) => pad(depth + 1) + writeJson(item, depth + 1, nl)).join(`,${nl}`)}${nl}${pad(depth)}]`;
  const record = value as Record<string, unknown>;
  const keys = Object.keys(record).filter((key) => record[key] !== undefined);
  return keys.length === 0
    ? '{}'
    : `{${nl}${keys.map((key) => `${pad(depth + 1)}${formatString(key)}: ${writeJson(record[key], depth + 1, nl)}`).join(`,${nl}`)}${nl}${pad(depth)}}`;
}

/** A whole reference file as the C# tool wrote it, byte-order mark and line endings included. */
export function writeFile(value: unknown, like: string): string {
  const nl = like.includes('\r\n') ? '\r\n' : '\n';
  const bom = like.startsWith('﻿') ? '﻿' : '';
  return bom + writeJson(value, 0, nl) + (like.endsWith(nl) ? nl : '');
}

const isAdded = (path: string): boolean => {
  const bare = path.replace(/\[\d+\]/g, '');
  return ADDED_IN_2_1.some((field) => bare.endsWith(`.${field}`));
};

/** The actual value without the fields the reference does not know. */
function strip(value: unknown, path: string): unknown {
  if (Array.isArray(value)) return value.map((item, i) => strip(item, `${path}[${i}]`));
  if (value === null || typeof value !== 'object') return value;
  const out: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(value)) if (!isAdded(`${path}.${key}`)) out[key] = strip(item, `${path}.${key}`);
  return out;
}

export interface Change {
  path: string;
  expected: unknown;
  actual: unknown;
}

/**
 * The reference with every differing value taken from `actual`; values within `tolerance` keep their
 * original digits. A list of another length is replaced as a whole.
 */
export function rebase(expected: unknown, actual: unknown, tolerance: number, path = '$', changes: Change[] = []): unknown {
  if (compare(expected, actual, tolerance, path).length === 0) return expected;
  if (Array.isArray(expected) && Array.isArray(actual) && expected.length === actual.length)
    return expected.map((item, i) => rebase(item, actual[i], tolerance, `${path}[${i}]`, changes));
  if (expected !== null && typeof expected === 'object' && !Array.isArray(expected) && actual !== null && typeof actual === 'object' && !Array.isArray(actual)) {
    const e = expected as Record<string, unknown>;
    const a = actual as Record<string, unknown>;
    for (const key of Object.keys(a))
      if (!(key in e) && !isAdded(`${path}.${key}`)) throw new Error(`${path}.${key}: neues Feld — erst in ADDED_IN_2_1 eintragen`);
    const out: Record<string, unknown> = {};
    for (const key of Object.keys(e)) {
      if (!(key in a)) throw new Error(`${path}.${key}: Feld entfällt — das kann der Abgleich nicht`);
      out[key] = rebase(e[key], a[key], tolerance, `${path}.${key}`, changes);
    }
    return out;
  }
  const value = strip(actual, path);
  changes.push({ path, expected, actual: value });
  return value;
}
