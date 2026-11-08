import { describe, expect, it } from 'vitest';
import { marketPains } from './market';

describe('market facts', () => {
  const facts = marketPains.flatMap((pain) => pain.facts);

  it('gives every fact a vendor page and the day it was read', () => {
    expect(facts.length).toBeGreaterThan(0);
    for (const fact of facts) {
      expect(fact.source.href, fact.vendor).toMatch(/^https:[/][/]/);
      expect(fact.asOf, fact.vendor).toMatch(/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/);
      expect(Number.isNaN(Date.parse(fact.asOf)), fact.vendor).toBe(false);
    }
  });

  it('states no price about ScreenHire: prices and limits come from the plan catalogue', () => {
    for (const pain of marketPains) {
      expect(pain.answer).not.toMatch(/[$£€] ?[0-9]/);
    }
  });
});
