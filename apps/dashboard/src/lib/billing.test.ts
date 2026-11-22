import { describe, expect, it } from 'vitest';
import { aiMonthText, aiUsageNotes, creditExpiryText, creditPurchaseStatus, creditSourceLabels, formatCredits, formatMoney, paymentMethodText, trialEnd, trialLine } from './billing.js';

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

describe('AI credits words', () => {
  it('shows credits in dollars to the cent, a balance a little below zero as it is', () => {
    expect(formatCredits(18.4)).toBe('$18.40');
    expect(formatCredits(-0.02)).toBe('-$0.02');
  });

  it('says how long credits last, in the singular for one month, and that they never expire when there is no limit', () => {
    expect(creditExpiryText(12)).toBe('Credits last 12 months from when they are added.');
    expect(creditExpiryText(1)).toBe('Credits last 1 month from when they are added.');
    expect(creditExpiryText(null)).toBe('Credits never expire.');
  });

  it('names where a credit came from and shows a refused reload as a failure, not as paid', () => {
    expect(creditSourceLabels).toEqual({ StartingCredit: 'Starting credit', Checkout: 'Bought', AutoReload: 'Auto-reload' });
    expect(creditPurchaseStatus.Failed).toEqual({ status: 'needsFixes', label: 'Failed' });
    expect(creditPurchaseStatus.Paid.label).toBe('Paid');
    expect(creditPurchaseStatus.Included.label).toBe('Included');
  });
});

describe('aiUsageNotes', () => {
  const usage = { month: '2026-11', builtInCalls: 4, charge: 0.2, callsWithUnknownCost: 0, ownKeyCalls: 0 };

  it("says nothing when every call was priced and on ScreenHire's key", () => {
    expect(aiUsageNotes(usage)).toEqual([]);
  });

  it('says that own-key and unpriced calls used no credits, in the singular for one', () => {
    expect(aiUsageNotes({ ...usage, ownKeyCalls: 1, callsWithUnknownCost: 1 })).toEqual([
      '1 call ran on your own key, so it used no credits.',
      '1 call has no known price, so it used no credits.',
    ]);
    expect(aiUsageNotes({ ...usage, ownKeyCalls: 3 })).toEqual(['3 calls ran on your own key, so they used no credits.']);
  });
});
