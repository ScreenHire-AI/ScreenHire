import { describe, expect, it } from 'vitest';
import { aiMonthText, formatMoney, paymentMethodText, trialEnd, trialLine } from './billing.js';

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

describe('formatMoney', () => {
  it('always shows the two decimals, as people read money, and none for a currency that has no minor unit', () => {
    expect(formatMoney(49, 'USD')).toBe('$49.00');
    expect(formatMoney(52.744, 'USD')).toBe('$52.74');
    expect(formatMoney(500, 'JPY')).toBe('¥500');
  });
});

describe('aiMonthText', () => {
  it('is the calls and the one amount for them, in two decimals', () => {
    expect(aiMonthText({ builtInCalls: 59, charge: 0.0547 })).toBe('59 AI calls, $0.05');
    expect(aiMonthText({ builtInCalls: 1, charge: 0 })).toBe('1 AI call, $0.00');
  });
});

describe('paymentMethodText', () => {
  it('names a card by its network and last four digits, and another method by its kind, never the provider', () => {
    expect(paymentMethodText({ type: 'card', brand: 'visa', last4: '4242' })).toBe('Visa ending in 4242');
    expect(paymentMethodText({ type: 'card', brand: 'amex', last4: '0005' })).toBe('American Express ending in 0005');
    expect(paymentMethodText({ type: 'card', brand: 'cartes_bancaires', last4: '1234' })).toBe('Card ending in 1234');
    expect(paymentMethodText({ type: 'sepa_debit', brand: null, last4: null })).toBe('Bank account');
    expect(paymentMethodText({ type: 'link', brand: null, last4: null })).toBe('Payment method on file');
  });
});
