import { describe, expect, it } from 'vitest';
import { PRICE_PLACEHOLDER, hasPrice, plans, pricedOffers, startingPrice, type Plan } from './pricing';

const plan = (name: string, monthlyPrice: string): Plan => ({ name, monthlyPrice, priceCurrency: 'USD', activeJobs: '', summary: '' });

describe('pricing', () => {
  it('ships with placeholders until the owner sets prices, so no invented number reaches the page', () => {
    expect(plans.every((p) => p.monthlyPrice === PRICE_PLACEHOLDER)).toBe(true);
    expect(pricedOffers(plans)).toEqual([]);
    expect(startingPrice(plans)).toBe(PRICE_PLACEHOLDER);
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
});
