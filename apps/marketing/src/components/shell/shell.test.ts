import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import { beforeAll, describe, expect, it } from 'vitest';
import { levels } from '../../lib/features';
import { buildPlans } from '../../lib/pricing';
import ApplicantPair from './ApplicantPair.astro';
import CtaLink from './CtaLink.astro';
import LevelChip from './LevelChip.astro';
import PlanRows from './PlanRows.astro';
import QuestionsList from './QuestionsList.astro';
import SiteFooter from './SiteFooter.astro';
import SiteHeader from './SiteHeader.astro';
import SourcedNumber from './SourcedNumber.astro';

let container: AstroContainer;
beforeAll(async () => {
  container = await AstroContainer.create();
});
const render = (component: Parameters<AstroContainer['renderToString']>[0], props: Record<string, unknown> = {}) =>
  container.renderToString(component, { props, request: new Request('http://localhost/pricing/') });

describe('LevelChip', () => {
  it('draws every level with the dashboard label, its icon and a text label', async () => {
    for (const [key, level] of Object.entries(levels)) {
      const html = await render(LevelChip, { level: key });
      expect(html).toContain(level.label);
      expect(html).toContain(`data-status-icon="${level.status}"`);
    }
    expect(Object.values(levels).map((level) => level.label)).toEqual(['No concerns', 'Review suggested', 'Verification suggested', 'Some signs of AI help']);
  });
});

describe('SiteHeader', () => {
  it('keeps the links in the bar and in a details menu, and needs no script', async () => {
    const html = await render(SiteHeader);
    expect(html).toContain('<details');
    expect(html).toContain('aria-label="Main"');
    expect(html).toContain('aria-label="Main menu"');
    const bar = html.match(/<nav aria-label="Main"[\s\S]*?<\/nav>/)![0];
    const menu = html.match(/<nav aria-label="Main menu"[\s\S]*?<\/nav>/)![0];
    const links = (markup: string) => [...markup.matchAll(/href="([^"]+)"/g)].map((match) => match[1]);
    expect(links(menu).slice(0, links(bar).length)).toEqual(links(bar));
    expect(html).not.toContain('<script');
  });

  it('marks the current page in both lists', async () => {
    const html = await render(SiteHeader);
    expect(html.match(/aria-current="page"/g)).toHaveLength(2);
  });

  it('puts the dark tokens on a dark header only', async () => {
    expect(await render(SiteHeader, { tone: 'dark' })).toMatch(/class="dark bg-background/);
    expect(await render(SiteHeader, { tone: 'light' })).not.toMatch(/class="dark /);
  });
});

describe('SiteFooter', () => {
  it('is a dark band with the footer links and the in-development note', async () => {
    const html = await render(SiteFooter);
    expect(html).toMatch(/<footer class="dark /);
    expect(html).toContain('aria-label="Footer"');
    expect(html).toContain('ScreenHire is in development');
  });
});

describe('CtaLink', () => {
  it('is a solid 8px link in the foreground colour, and indigo only when asked', async () => {
    const solid = await render(CtaLink, { href: '/x/' });
    expect(solid).toContain('rounded-lg');
    expect(solid).toContain('bg-foreground');
    expect(solid).not.toContain('rounded-full');
    expect(await render(CtaLink, { href: '/x/', variant: 'indigo' })).toContain('bg-primary');
  });
});

describe('ApplicantPair', () => {
  it('shows the real and the flagged applicant with why, but also and next step, and no codes', async () => {
    const html = await render(ApplicantPair);
    for (const text of ['No concerns', 'Verification suggested', 'Why', 'But also', 'Next step', 'A person on your team decides']) {
      expect(html).toContain(text);
    }
    expect(html).not.toMatch(/\b[A-Z]{2,}(?:_[A-Z]+)+\b/);
    expect(html).not.toMatch(/score|%/i);
  });
});

describe('PlanRows', () => {
  it('shows the placeholder until the catalogue has a price, then the price with its limit', async () => {
    expect(await render(PlanRows)).toContain('[YOUR PRICE]');
    const priced = buildPlans([
      { key: 'starter', name: 'Starter', order: 1, monthlyPrice: 79, currency: 'USD', activeJobs: 3, unlimited: false, stripePriceId: null },
      { key: 'unlimited', name: 'Unlimited', order: 2, monthlyPrice: 249, currency: 'USD', activeJobs: null, unlimited: true, stripePriceId: null },
    ]);
    const html = await render(PlanRows, { plans: priced });
    expect(html).toContain('$79');
    expect(html).toContain('3 active jobs');
    expect(html).toContain('Unlimited active jobs');
    expect(html).not.toContain('[YOUR PRICE]');
  });
});

describe('SourcedNumber', () => {
  it('links the source beside the number', async () => {
    const html = await render(SourcedNumber, { figure: { value: '31%', claim: 'a claim', source: { label: 'Checkr, 2025', href: 'https://example.org/x' } } });
    expect(html).toContain('31%');
    expect(html).toContain('href="https://example.org/x"');
    expect(html).toContain('Checkr, 2025');
  });
});

describe('QuestionsList', () => {
  it('lists each question under a heading and writes no structured data', async () => {
    const html = await render(QuestionsList, { faqs: [{ question: 'Is it fair?', answer: 'Yes.' }] });
    expect(html).toContain('Is it fair?');
    expect(html).not.toContain('application/ld+json');
  });
});
