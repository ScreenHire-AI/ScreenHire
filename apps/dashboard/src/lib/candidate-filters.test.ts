import { describe, expect, it } from 'vitest';
import { readReached, reachedLabel } from '#lib/candidate-filters.js';

const from = '2026-10-01T00:00:00.000Z';
const to = '2026-10-31T00:00:00.000Z';

describe('a report row\'s link to the candidates behind it', () => {
  it('reads the stage and dates from the address', () => {
    expect(readReached(new URLSearchParams(`reached=Interview&from=${from}&to=${to}`))).toEqual({ kind: 'Interview', from, to });
  });

  it('reads nothing from an address that is not one', () => {
    for (const search of [
      '',
      `reached=Interview&from=${from}`,
      `reached=Verification&from=${from}&to=${to}`,
      `reached=Custom&from=${from}&to=${to}`,
      `reached=Interview&from=nonsense&to=${to}`,
      `reached=Interview&from=${to}&to=${from}`,
    ]) {
      expect(readReached(new URLSearchParams(search)), search).toBeNull();
    }
  });

  it('says the filter in words, ending on the last day, not the day after', () => {
    const label = reachedLabel({ kind: 'Interview', from, to });
    expect(label).toMatch(/^Reached Interview, /);
    expect(label).toContain('30');
    expect(label).not.toContain('31');
  });
});
