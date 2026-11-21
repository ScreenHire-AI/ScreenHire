// The careers footer's credit carries the copyright year from the clock, never a typed year (the owner's walk-through,
// 2026-11-21).
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import { afterEach, describe, expect, it, vi } from 'vitest';
import PoweredBy from './PoweredBy.astro';

afterEach(() => vi.useRealTimers());

describe('PoweredBy', () => {
  it('names the year the page is served in', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2027-01-01T00:30:00Z'));
    const container = await AstroContainer.create();
    expect(await container.renderToString(PoweredBy)).toContain('Powered by ScreenHire © 2027');
  });
});
