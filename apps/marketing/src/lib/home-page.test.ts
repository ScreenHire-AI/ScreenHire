import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

// The home page's own rules (docs/design/marketing-site.md, design system section 22). It reads its prices, job limits
// and trial length from the plan catalogue and its sourced numbers from lib/features.ts, so the page file writes none.
// These tests read the page's source, so the order test is tied to the names index.astro gives the five points.
const home = readFileSync(new URL('../pages/index.astro', import.meta.url), 'utf8');

describe('home page', () => {
  it('never shows the captured verification panels, which name the kinds of signal a check uses', () => {
    expect(home).not.toMatch(/verification-(?:flagged|clear)/);
  });

  it('writes no price, job limit or trial length of its own', () => {
    expect(home).not.toMatch(/\$\s?\d/);
    expect(home).not.toMatch(/\b\d+[- ]days?\b/i);
    expect(home).not.toMatch(/\b\d+ open jobs?\b/i);
  });

  it('opens with the one h1 on a dark header, and puts the sign-up address through appLinks', () => {
    expect(home.match(/<h1\b/g)).toHaveLength(1);
    expect(home).toContain('headerTone="dark"');
    expect(home).toContain('appLinks(PUBLIC_APP_URL');
  });

  it('tells the five selling points in the owner\'s order', () => {
    const order = ['realPeople', 'neverGuessing', 'automate', 'integrationsPoint', 'builtYourWay'].map((name) => home.indexOf(`<SellingPoint point={${name}!}`));
    expect(order.every((index) => index > 0)).toBe(true);
    expect([...order].sort((a, b) => a - b)).toEqual(order);
  });

  it('gives every screenshot to ProductFrame, so each has alt text, a size and a phone layout', () => {
    expect(home).not.toMatch(/<(?:img|Image|Picture)\b/);
    const screenshots = home.match(/<ProductFrame\b[^>]*\bsrc=/gs) ?? [];
    expect(screenshots.length).toBeGreaterThan(0);
    expect(home.match(/<ProductFrame\b[^>]*\bphoneSrc=/gs)).toHaveLength(screenshots.length);
  });

  it('leaves out the captured activity and workflow screens, which show a matching card, a rejection review and a verification tag', () => {
    expect(home).not.toMatch(/assets\/product\/(?:candidate-activity|workflow)/);
  });
});
