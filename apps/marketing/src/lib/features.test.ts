import { describe, expect, it } from 'vitest';
import { automation, everyPlanIncludes, hero, integrations, notPaidExtra, sellingPoints } from './features';

// The site's own words. The same rules are in docs/design/marketing-site.md (copy rules) and CLAUDE.md (non-negotiables 1 to 3).
const bannedWords = ['AI-powered', 'supercharge', 'revolutionize', 'seamless', 'unlock', 'effortless', 'next-generation'];
const words = JSON.stringify({ sellingPoints, hero, everyPlanIncludes, notPaidExtra, integrations, automation });

describe('selling points', () => {
  it("are the owner's five, in order", () => {
    expect(sellingPoints.map((point) => point.id)).toEqual(['real-people', 'never-guessing', 'automate-anything', 'integrations', 'built-your-way']);
  });

  it('each tell the pain, the promise and the proof, and link a page', () => {
    for (const point of sellingPoints) {
      expect(point.title.length).toBeGreaterThan(0);
      expect(point.pain.length).toBeGreaterThan(0);
      expect(point.promise.length).toBeGreaterThan(0);
      expect(point.proof.length).toBeGreaterThan(0);
      expect(point.pagePath).toMatch(/^\/[a-z0-9-]+\/(#[a-z-]+)?$/);
    }
  });

  it('give every number a source the page can link, with its year', () => {
    const figures = sellingPoints.flatMap((point) => point.figures);
    expect(figures.length).toBeGreaterThan(0);
    for (const figure of figures) {
      expect(figure.source.href).toMatch(/^https:\/\//);
      expect(figure.source.label).toMatch(/\b20\d\d\b/);
      expect(figure.value.length).toBeGreaterThan(0);
    }
  });
});

describe("the site's words", () => {
  it('never use the AI fluff words', () => {
    for (const word of bannedWords) expect(words.toLowerCase(), word).not.toContain(word.toLowerCase());
  });

  it('use no em-dash, en-dash, curly quote or middle dot between facts (M5)', () => {
    expect(words).not.toMatch(/[\u00B7\u2013\u2014\u2018\u2019\u201C\u201D]/);
  });

  it('show no reason code, score or percentage of AI, and never say ScreenHire rejects', () => {
    expect(words).not.toMatch(/\b[A-Z]{2,}(?:_[A-Z]+)+\b/);
    expect(words).not.toMatch(/\bscore\b|% of AI|\bAI-written\b/i);
    expect(words).not.toMatch(/ScreenHire (?:will )?rejects?/i);
  });

  it('name AI only where it names a feature', () => {
    const mentions = words.match(/[^.]*\bAI\b[^.]*/g) ?? [];
    for (const sentence of mentions) expect(sentence).toMatch(/AI assistants|AI key|AI consistency check|AI that helps|signs of AI help|your own AI/);
  });

  it('write the automation example as When, If, then what happens', () => {
    expect(automation.sentence).toMatch(/^When .*, if .*,? .+\.$/);
    expect(automation.examples.map((example) => example.scope)).toEqual(['One step', 'A few steps', 'Your whole process']);
  });
});
