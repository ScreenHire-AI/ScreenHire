import { vi } from 'vitest';

/**
 * Answers `matchMedia` width queries as a window `width` pixels wide would, so the sidebar and the shell's MediaQuery
 * see a phone, a tablet or a laptop. jsdom has no media queries of its own. Restore with `vi.unstubAllGlobals()`.
 */
export function stubViewport(width: number): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: matchesWidth(query, width),
    media: query,
    onchange: null,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
    dispatchEvent: () => false,
  }));
}

function matchesWidth(query: string, width: number): boolean {
  const conditions = [...query.matchAll(/\((min|max)-width:\s*(\d+)px\)/g)];
  return conditions.length > 0 && conditions.every(([, kind, value]) => (kind === 'min' ? width >= Number(value) : width <= Number(value)));
}
