import { describe, expect, it } from 'vitest';
import { banVerdict, joinGerman, percent, pickVerdict, points, sampleText, standing, termVerdict } from './format';

describe('format', () => {
  it('writes German numbers', () => {
    expect(percent(0.5347, 1)).toBe('53,5 %');
    expect(percent(0.5347, 0)).toBe('53 %');
    expect(points(1.27)).toBe('+1,3');
    expect(points(-0.4)).toBe('−0,4');
  });

  it('judges against place 1, not against 50 %', () => {
    expect(standing(0.55, 0.04, 0.549, 0.04)).toBe('tied');
    expect(standing(0.6, 0.01, 0.5, 0.01)).toBe('clear');
    expect(pickVerdict(true, 1, 'tied').text).toBe('bester Pick der Liste');
    expect(pickVerdict(true, 3, 'tied').text).toBe('gleichauf mit Platz 1');
    expect(pickVerdict(false, 1, 'clear').text).toBe('Datenlage zu dünn');
  });

  it('keeps the 1.5 thresholds', () => {
    expect(banVerdict(3.6).text).toBe('wichtiger Ban');
    expect(banVerdict(2.6).text).toBe('sinnvoller Ban');
    expect(banVerdict(2.4).text).toBe('');
    expect(termVerdict(null)).toBe('keine Daten');
    expect(termVerdict(0)).toBe('neutral');
    expect(termVerdict(-2.5)).toBe('stark dagegen');
    expect(sampleText(0.52, 49)).toBe('dünne Datenlage · 49 Spiele');
    expect(joinGerman(['Botlane', 'Support'])).toBe('Botlane und Support');
  });
});
