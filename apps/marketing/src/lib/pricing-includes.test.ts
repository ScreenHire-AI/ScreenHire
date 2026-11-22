import { included } from '@screenhire/plans';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import { describe, expect, it } from 'vitest';
import PricingPage from '../pages/pricing.astro';

// What every plan includes comes from the plan catalogue (packages/plans/included.json), and the dashboard's Billing page shows the
// same export on its plan card and each plan's card (apps/dashboard's plan-card.test.ts). This renders the real pricing page, so a
// page that stopped reading the catalogue, or a list written into the page, fails here.
describe('pricing page', () => {
  it("lists what every plan includes from the plan catalogue, in the catalogue's order, under the plans", async () => {
    const container = await AstroContainer.create();
    const html = await container.renderToString(PricingPage, { request: new Request('http://localhost/pricing/') });

    const list = html.match(/<ul aria-label="Every plan includes"[^>]*>([\s\S]*?)<\/ul>/)?.[1] ?? '';
    const items = [...list.matchAll(/<li[^>]*>[\s\S]*?<\/svg>\s*([\s\S]*?)\s*<\/li>/g)].map((match) => match[1]);
    expect(included.length).toBeGreaterThan(0);
    expect(items).toEqual(included);
    expect(html.indexOf('aria-label="Plans"')).toBeLessThan(html.indexOf('aria-label="Every plan includes"'));
  });
});
