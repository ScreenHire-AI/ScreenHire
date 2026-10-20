import { describe, expect, it } from 'vitest';
import { footerNav, mainNav, notFound, pages } from './pages';

const all = Object.values(pages);

describe('page registry', () => {
  it('gives every page a unique title under about 60 characters', () => {
    const titles = [...all.map((page) => page.title), notFound.title];
    expect(new Set(titles).size).toBe(titles.length);
    for (const title of titles) {
      expect(title.length, title).toBeLessThanOrEqual(60);
    }
  });

  it('keeps descriptions unique and under about 160 characters', () => {
    const descriptions = all.map((page) => page.description);
    expect(new Set(descriptions).size).toBe(descriptions.length);
    for (const description of descriptions) {
      expect(description.length, description).toBeLessThanOrEqual(160);
    }
  });

  it('uses unique site-relative paths with a trailing slash, matching trailingSlash: always', () => {
    const paths = all.map((page) => page.path);
    expect(new Set(paths).size).toBe(paths.length);
    for (const path of paths) {
      expect(path).toMatch(/^\/([a-z0-9-]+\/)*$/);
    }
  });

  it('only links navigation to registered pages', () => {
    for (const page of [...mainNav, ...footerNav]) {
      expect(all).toContain(page);
    }
  });

  it('writes no em dashes, en dashes or curly quotes (.claude/rules/writing-style.md)', () => {
    const text = JSON.stringify([all, notFound]);
    expect(text).not.toMatch(/[–—‘’“”]/);
  });
});
