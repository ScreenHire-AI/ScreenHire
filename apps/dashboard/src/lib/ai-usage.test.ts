import { describe, expect, it } from 'vitest';
import { formatAmount, formatCost } from './ai-usage.js';

// Money reads in two decimals everywhere AI use is shown (the owner's walk-through, 2026-11-21), and a call that cost
// something never reads as free.
describe('AI usage money', () => {
  it('rounds to cents', () => {
    expect(formatCost(0.0547)).toBe('$0.05');
    expect(formatAmount(3.2, 'Money')).toBe('$3.20');
  });

  it('says under a cent rather than $0.00 for a cost above zero', () => {
    expect(formatCost(0.0008)).toBe('Under $0.01');
    expect(formatCost(0)).toBe('$0.00');
  });

  it('keeps an unpriced cost as Unknown', () => {
    expect(formatCost(null)).toBe('Unknown');
  });
});
