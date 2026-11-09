// The board's job card (docs/design/redesign-2026-11.md, "Setup picks" 6): the title, the short summary clamped to three
// lines with CSS alone, then at most three chips. Employer colours never restyle it, and it adds no script.
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import { beforeAll, describe, expect, it } from 'vitest';
import JobCard from './JobCard.astro';

let container: AstroContainer;

beforeAll(async () => {
  container = await AstroContainer.create();
});

const job = {
  title: 'Service plumber',
  summary: 'Fix leaks, service boilers and fit bathrooms for homes across Leeds.',
  location: 'Leeds, England, GB',
  workplaceType: 'Onsite',
  employmentType: 'FullTime',
  salaryMin: 34_000,
  salaryMax: 40_000,
  salaryCurrency: 'GBP',
};

const render = (overrides: Partial<typeof job> = {}) =>
  container.renderToString(JobCard, { props: { job: { ...job, ...overrides }, href: '/brightwater/jobs/service-plumber' } });

describe('JobCard', () => {
  it('is one link that reads title, summary, then the chips', async () => {
    const html = await render();

    expect(html.match(/<a /g)).toHaveLength(1);
    expect(html).toContain('href="/brightwater/jobs/service-plumber"');
    const title = html.indexOf('Service plumber');
    const summary = html.indexOf('Fix leaks, service boilers');
    const firstChip = html.indexOf('Leeds, England, GB</span>');
    expect(title).toBeGreaterThan(-1);
    expect(summary).toBeGreaterThan(title);
    expect(firstChip).toBeGreaterThan(summary);
  });

  it('clamps the summary to three lines with CSS, not script', async () => {
    const html = await render();

    expect(html).toMatch(/<span class="[^"]*\bline-clamp-3\b[^"]*">Fix leaks/);
    expect(html).not.toContain('<script');
  });

  it('shows at most three chips: place, type and pay', async () => {
    const html = await render({ workplaceType: 'Hybrid' });

    const chips = [...html.matchAll(/<span class="[^"]*\bbg-candidate-chip\b[^"]*">([^<]*)<\/span>/g)].map((match) => match[1]);
    expect(chips).toEqual(['Leeds, England, GB (Hybrid)', 'Full-time', '£34k–£40k']);
  });

  it('draws no summary line when the job has none', async () => {
    const html = await render({ summary: '' });

    expect(html).not.toContain('line-clamp');
    expect(html).toContain('Service plumber');
  });

  it('escapes the summary', async () => {
    const html = await render({ summary: 'Fix <b>leaks</b> & more.' });

    expect(html).toContain('Fix &lt;b&gt;leaks&lt;/b&gt; &amp; more.');
  });

  it('takes no employer colour: only the neutral candidate tokens, and no inline style', async () => {
    const html = await render();

    expect(html).not.toMatch(/candidate-(cta|brand|header)/);
    expect(html).not.toContain('style=');
  });
});
