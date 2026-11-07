import { describe, expect, it } from 'vitest';
import type { CataloguePlan } from '@screenhire/plans';
import { PRICE_PLACEHOLDER, buildPlans, hasPrice, plans, pricedOffers, startingPrice, type Plan } from './pricing';

const plan = (name: string, monthlyPrice: string): Plan => ({ name, monthlyPrice, priceCurrency: 'USD', activeJobs: '', summary: '' });

describe('pricing', () => {
  it('ships with placeholders until the owner sets prices, so no invented number reaches the page', () => {
    expect(plans.every((p) => p.monthlyPrice === PRICE_PLACEHOLDER)).toBe(true);
    expect(pricedOffers(plans)).toEqual([]);
    expect(startingPrice(plans)).toBe(PRICE_PLACEHOLDER);
  });

  it('shows the limit placeholder for the shipped limited plans and "Unlimited" for the unlimited one', () => {
    expect(plans.map((p) => p.activeJobs)).toEqual(['[YOUR LIMIT] active jobs', '[YOUR LIMIT] active jobs', 'Unlimited active jobs']);
    expect(plans.map((p) => p.name)).toEqual(['Starter', 'Growth', 'Unlimited']);
    expect(plans.filter((p) => p.highlighted).map((p) => p.name)).toEqual(['Growth']);
  });

  it.each([
    ['99', true],
    ['49.50', true],
    [PRICE_PLACEHOLDER, false],
    ['', false],
    ['99 USD', false],
  ])('treats %j as a price: %s', (price, expected) => {
    expect(hasPrice(plan('P', price))).toBe(expected);
  });

  it('offers only priced plans and starts from the cheapest', () => {
    const mixed = [plan('A', '149'), plan('B', PRICE_PLACEHOLDER), plan('C', '99')];
    expect(pricedOffers(mixed).map((offer) => offer.name)).toEqual(['A', 'C']);
    expect(startingPrice(mixed)).toBe('$99');
  });

  describe('from a filled-in catalogue', () => {
    const entry = (key: string, order: number, monthlyPrice: number | null, activeJobs: number | null, unlimited = false): CataloguePlan => ({
      key,
      name: key[0].toUpperCase() + key.slice(1),
      order,
      monthlyPrice,
      currency: 'EUR',
      activeJobs,
      unlimited,
      stripePriceId: null,
    });
    const filled = [entry('starter', 1, 49, 1), entry('growth', 2, 149.5, 10), entry('unlimited', 3, 399, null, true)];

    it('shows prices, currency and the limit text', () => {
      const built = buildPlans(filled);
      expect(built.map((p) => p.monthlyPrice)).toEqual(['49', '149.50', '399']);
      expect(built.every((p) => p.priceCurrency === 'EUR')).toBe(true);
      expect(built.map((p) => p.activeJobs)).toEqual(['1 active job', '10 active jobs', 'Unlimited active jobs']);
      expect(built.every(hasPrice)).toBe(true);
    });

    it('offers every priced plan and starts from the lowest', () => {
      const built = buildPlans(filled);
      expect(pricedOffers(built)).toEqual([
        { name: 'Starter', price: '49', priceCurrency: 'EUR' },
        { name: 'Growth', price: '149.50', priceCurrency: 'EUR' },
        { name: 'Unlimited', price: '399', priceCurrency: 'EUR' },
      ]);
      expect(startingPrice(built)).toBe('$49');
    });

    it('keeps an unpriced plan out of the offers and ignores a limit on an unlimited plan', () => {
      const built = buildPlans([entry('starter', 1, null, 3), entry('growth', 2, 99, 5), entry('unlimited', 3, null, 7, true)]);
      expect(pricedOffers(built).map((o) => o.name)).toEqual(['Growth']);
      expect(startingPrice(built)).toBe('$99');
      expect(built[0].monthlyPrice).toBe(PRICE_PLACEHOLDER);
      expect(built[2].activeJobs).toBe('Unlimited active jobs');
    });
  });
});
