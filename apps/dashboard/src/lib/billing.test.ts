import { describe, expect, it } from 'vitest';
import { trialEnd, trialLine } from './billing.js';

describe('trialLine', () => {
  it('says how many days the trial lasts and that it needs no card', () => {
    expect(trialLine(14)).toBe('Your free trial lasts 14 days and needs no card.');
    expect(trialLine(1)).toBe('Your free trial lasts 1 day and needs no card.');
  });

  it('says nothing where there is no trial, which is how billing being off reads', () => {
    expect(trialLine(0)).toBe('');
    expect(trialLine(-3)).toBe('');
  });
});

describe('trialEnd', () => {
  const now = Date.parse('2026-11-21T12:00:00Z');
  const trialing = { available: true, state: 'Trialing', trialEndsAt: '2026-12-05T09:00:00Z' } as const;

  it('is the end of a trial that is still running', () => {
    expect(trialEnd(trialing, now)).toBe('2026-12-05T09:00:00Z');
  });

  it.each([
    ['billing is off', { ...trialing, available: false }],
    ['a plan is paid for', { available: true, state: 'Active', trialEndsAt: null }],
    ['the payment failed', { available: true, state: 'PastDue', trialEndsAt: null }],
    ['the trial ended', { available: true, state: 'Ended', trialEndsAt: null }],
    ['the end has passed but the state has not caught up', { ...trialing, trialEndsAt: '2026-11-21T11:59:00Z' }],
  ] as const)('is nothing once %s', (_, overview) => {
    expect(trialEnd(overview, now)).toBeNull();
  });

  it('is nothing while the answer has not arrived, which is all anyone but an Owner ever has (only an Owner asks for billing)', () => {
    expect(trialEnd(undefined, now)).toBeNull();
  });
});
