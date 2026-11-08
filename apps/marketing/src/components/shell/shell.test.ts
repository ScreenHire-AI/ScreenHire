import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import { PUBLIC_APP_URL } from 'astro:env/server';
import { beforeAll, describe, expect, it } from 'vitest';
import { appLinks } from '../../lib/app-links';
import { levels } from '../../lib/features';
import { pages, productNav } from '../../lib/pages';
import { buildPlans } from '../../lib/pricing';
import portal from '../../assets/product/candidate-portal.png';
import portalPhone from '../../assets/product/candidate-portal-phone.png';
import ApplicantPair from './ApplicantPair.astro';
import ClosingBand from './ClosingBand.astro';
import CtaLink from './CtaLink.astro';
import LevelChip from './LevelChip.astro';
import PageHero from './PageHero.astro';
import PageIntro from './PageIntro.astro';
import PlanRows from './PlanRows.astro';
import ProductFrame from './ProductFrame.astro';
import ProofList from './ProofList.astro';
import QuestionsList from './QuestionsList.astro';
import RuledList from './RuledList.astro';
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

  it('lists the overview and every product page under Product, in the bar and in the phone menu', async () => {
    const html = await render(SiteHeader);
    const bar = html.match(/<nav aria-label="Main"[\s\S]*?<\/nav>/)![0];
    const menu = html.match(/<nav aria-label="Main menu"[\s\S]*?<\/nav>/)![0];
    for (const page of productNav) {
      expect(bar, page.path).toContain(`href="${page.path}"`);
      expect(menu, page.path).toContain(`href="${page.path}"`);
    }
    expect(productNav.map((page) => page.path)).toEqual([
      '/product/',
      '/product/real-people/',
      '/product/never-guessing/',
      '/product/automate-anything/',
      '/product/integrations/',
      '/product/built-your-way/',
      '/product/careers-site/',
    ]);
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

  it('says what kind of thing a check looks at, never which signals it uses (docs/design/marketing-site.md)', async () => {
    const html = await render(ApplicantPair);
    expect(html).not.toMatch(/\b(?:hosting|data ?cent(?:er|re)|VoIP|internet line|disposable|proxy|VPN|network|threshold|near-identical)\b/i);
  });
});

describe('PlanRows', () => {
  it('shows the price with its limit, "any number" for the unlimited plan, and no amount for a plan without a price', async () => {
    const priced = buildPlans([
      { key: 'starter', name: 'Starter', order: 1, monthlyPrice: 79, currency: 'USD', activeJobs: 3, unlimited: false, stripePriceId: null },
      { key: 'unlimited', name: 'Unlimited', order: 2, monthlyPrice: 249, currency: 'USD', activeJobs: null, unlimited: true, stripePriceId: null },
    ]);
    const html = await render(PlanRows, { plans: priced });
    expect(html).toContain('$79');
    expect(html).toContain('3\u00A0open jobs');
    expect(html).toContain('Any number of open jobs');
    expect(html).toContain('$249');

    const unpriced = buildPlans([
      { key: 'starter', name: 'Starter', order: 1, monthlyPrice: null, currency: 'USD', activeJobs: 3, unlimited: false, stripePriceId: null },
      { key: 'growth', name: 'Growth', order: 2, monthlyPrice: null, currency: 'USD', activeJobs: null, unlimited: false, stripePriceId: null },
    ]);
    const bare = await render(PlanRows, { plans: unpriced });
    expect(bare).toContain('Starter');
    expect(bare).toContain('3\u00A0open jobs');
    expect(bare).toContain('Growth');
    expect(bare).not.toMatch(/\$|a month|\[YOUR/);
  });

  it('shows the real catalogue with no placeholder, and names the trial plan', async () => {
    const html = await render(PlanRows, { trialPlan: 'Growth' });
    expect(html).not.toContain('[YOUR');
    expect(html).toContain('(your trial)');
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

describe('PageIntro', () => {
  it('has the one h1 on the white page, or on the dark band for a page that sells', async () => {
    const light = await render(PageIntro, { title: 'A title', lead: 'A lead.' });
    expect(light).toContain('<h1');
    expect(light).toContain('text-section');
    expect(light).toContain('bg-page');
    expect(light).not.toContain('kicker');
    const dark = await render(PageIntro, { title: 'A title', tone: 'dark' });
    expect(dark).toMatch(/class="dark /);
  });

  it('drops its bottom space when joined to a white section, which has its own', async () => {
    const header = (html: string) => /<header[^>]*class="([^"]*)"/.exec(html)![1]!;
    expect(header(await render(PageIntro, { title: 'A title' }))).toContain('pb-12');
    expect(header(await render(PageIntro, { title: 'A title', joined: true }))).not.toContain('pb-');
    expect(header(await render(PageIntro, { title: 'A title', tone: 'dark' }))).toContain('pb-12');
  });
});

describe('ClosingBand', () => {
  it('sends the call to action where the header sends Start free trial, unless told where', async () => {
    // The sign-up address, or the pricing page when no dashboard address is configured.
    const signUp = appLinks(PUBLIC_APP_URL, pages.pricing.path).signUp;
    expect(await render(ClosingBand, { title: 'Close.' })).toContain(`href="${signUp}"`);
    expect(await render(ClosingBand, { title: 'Close.', href: '/x/' })).toContain('href="/x/"');
  });
});

describe('PageHero', () => {
  it('has the one h1 at the display size on the dark band, with the call to action and a link to pricing', async () => {
    const html = await render(PageHero, { title: 'A title', lead: 'A lead.' });
    expect(html.match(/<h1/g)).toHaveLength(1);
    expect(html).toContain('text-display');
    expect(html).toMatch(/class="dark /);
    // The trial's length comes from the catalogue (packages/plans/trial.json).
    expect(html).toContain('Start your free 14-day trial');
    expect(html).toContain(`href="${pages.pricing.path}"`);
    expect(html).not.toContain('kicker');
  });

  it('sets a headline of more than 40 characters at the section size, so a sentence does not fill the first screen', async () => {
    const h1 = (html: string) => /<h1[^>]*class="([^"]*)"/.exec(html)![1]!;
    const long = await render(PageHero, { title: "Know it's a real person before you book the interview.", lead: 'A lead.' });
    expect(h1(long)).toContain('text-section');
    expect(h1(long)).not.toContain('text-display');
    const short = await render(PageHero, { title: 'Automate anything', lead: 'A lead.' });
    expect(h1(short)).toContain('text-display');
  });
});

describe('RuledList', () => {
  it('reads each term with its detail in a description list', async () => {
    const html = await render(RuledList, { items: [{ term: 'Email', detail: 'Replies stay on the candidate.' }] });
    expect(html).toContain('<dl');
    expect(html).toContain('<dt');
    expect(html).toContain('Email');
    expect(html).toContain('Replies stay on the candidate.');
  });

  it('stops a detail at the paragraph measure, so a full-width list keeps a readable line', async () => {
    const html = await render(RuledList, { items: [{ term: 'Email', detail: 'Replies stay on the candidate.' }] });
    expect(/<dd[^>]*class="([^"]*)"/.exec(html)![1]).toContain('max-w-measure');
  });
});

describe('ProofList', () => {
  it('lists each proof point', async () => {
    const html = await render(ProofList, { items: ['A check on every application', 'A person decides'] });
    expect(html.match(/<li/g)).toHaveLength(2);
  });
});

describe('ProductFrame', () => {
  const alt = 'The candidate page.';

  it('frames markup in the light theme with one edge', async () => {
    const html = await render(ProductFrame);
    expect(html).toContain('light ');
    expect(html).toContain('rounded-panel');
    expect(html).not.toContain('<picture');
  });

  it('draws a screenshot as AVIF with a WebP fallback, with its size taken from the file', async () => {
    const html = await render(ProductFrame, { src: portal, alt });
    expect(html).toMatch(/<source[^>]* type="image\/avif"/);
    expect(html).toMatch(/<img[^>]* src="[^"]+f=webp"/);
    expect(html).toMatch(/<img[^>]* width="\d+"/);
    expect(html).toMatch(/<img[^>]* height="\d+"/);
    expect(html).toContain(`alt="${alt}"`);
  });

  it('loads lazily, and eagerly with high priority only for the one hero image', async () => {
    const lazy = await render(ProductFrame, { src: portal, alt });
    expect(lazy).toContain('loading="lazy"');
    expect(lazy).not.toContain('fetchpriority="high"');
    const hero = await render(ProductFrame, { src: portal, alt, priority: true });
    expect(hero).toContain('loading="eager"');
    expect(hero).toContain('fetchpriority="high"');
  });

  it('adds the phone layout as AVIF and WebP sources with their own size, taken below md', async () => {
    const html = await render(ProductFrame, { src: portal, phoneSrc: portalPhone, alt });
    const phone = [...html.matchAll(/<source[^>]* media="\(max-width: 47\.99rem\)"[^>]*>/g)].map((match) => match[0]);
    expect(phone).toHaveLength(2);
    expect(phone.join('')).toMatch(/image\/avif/);
    expect(phone.join('')).toMatch(/image\/webp/);
    for (const source of phone) {
      expect(source).toMatch(/width="\d+"/);
      expect(source).toMatch(/height="\d+"/);
    }
  });

  it('refuses a screenshot without alt text (F7)', async () => {
    await expect(render(ProductFrame, { src: portal })).rejects.toThrow(/alt text/);
  });
});
