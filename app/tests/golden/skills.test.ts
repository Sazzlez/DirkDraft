import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { extendSkills } from '../../src/core/skillplan';

interface Golden {
  cases: { input: (string | null)[]; output: { level: number; ability: string; isDerived: boolean }[] }[];
}

const golden = JSON.parse(readFileSync(new URL('./builds/skills.json', import.meta.url), 'utf8')) as Golden;

describe('golden: SkillPlan.Extend', () => {
  golden.cases.forEach((c, index) => {
    it(`case ${index + 1}`, () => {
      expect(extendSkills(c.input)).toEqual(c.output);
    });
  });
});
