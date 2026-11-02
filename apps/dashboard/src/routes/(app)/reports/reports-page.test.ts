import type { HiringReport } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { goto } from '$app/navigation';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { downloadCsv } from '#lib/csv.js';
import { previousInstants, rangeInstants } from '#lib/report-range.js';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, jobs, user } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const location = vi.hoisted(() => ({ url: new URL('http://dashboard.test/reports') }));
vi.mock('$app/state', () => ({
  page: {
    data: { user },
    get url() {
      return location.url;
    },
  },
}));
vi.mock('$app/navigation', () => ({ goto: vi.fn(async () => {}) }));
vi.mock('#lib/csv.js', async (importOriginal) => ({ ...(await importOriginal<typeof import('#lib/csv.js')>()), downloadCsv: vi.fn() }));

const report: HiringReport = {
  from: '2026-10-01T00:00:00Z',
  to: '2026-10-31T00:00:00Z',
  headline: { applications: 8, reachedInterview: 3, offers: 1, hires: 3, medianDaysToHire: 21.4 },
  previous: { applications: 5, reachedInterview: 4, offers: 1, hires: 3, medianDaysToHire: 24 },
  weekly: [
    { weekStart: '2026-10-05', applications: 3, hires: 0 },
    { weekStart: '2026-10-12', applications: 5, hires: 2 },
  ],
  funnel: [
    { kind: 'Applied', applications: 8, movedOn: 5 },
    { kind: 'Screening', applications: 5, movedOn: 3 },
    { kind: 'Interview', applications: 3, movedOn: 1 },
    { kind: 'Hired', applications: 1, movedOn: null },
  ],
  timeToHire: {
    stages: [
      { kind: 'Applied', medianDays: 2, hires: 3 },
      { kind: 'Interview', medianDays: 9.5, hires: 3 },
    ],
    jobs: [
      { jobId: jobs[0]!.id, jobTitle: jobs[0]!.title, hires: 2, medianDays: 14 },
      { jobId: jobs[1]!.id, jobTitle: jobs[1]!.title, hires: 1, medianDays: 30 },
    ],
  },
  sources: [
    { key: 'careers', name: 'Careers page', applications: 6, hires: 1 },
    { key: 'website', name: 'Your website', applications: 2, hires: 0 },
  ],
  openJobs: [{ jobId: jobs[0]!.id, title: jobs[0]!.title, applications: 6, inInterview: 2, daysOpen: 12, hires: 1 }],
};

const nothing: HiringReport = {
  ...report,
  headline: { applications: 0, reachedInterview: 0, offers: 0, hires: 0, medianDaysToHire: null },
  previous: null,
  weekly: [],
  funnel: [],
  timeToHire: { stages: [], jobs: [] },
  sources: [],
  openJobs: [],
};

/** Answers the report with `body` and the jobs list, and records each report request's address. */
function renderPage(body: HiringReport = report) {
  const asked: URL[] = [];
  const others = apiStub({ 'GET /v1/jobs': () => Response.json(jobs) });
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).startsWith('/v1/reports/hiring')) {
        asked.push(new URL(String(input), 'http://dashboard.test'));
        return Response.json(body);
      }
      return others(input, init);
    }),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return asked;
}

beforeEach(() => {
  location.url = new URL('http://dashboard.test/reports');
  vi.mocked(goto).mockClear();
  vi.mocked(downloadCsv).mockClear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('reports page', () => {
  it('shows five headline numbers, each with its change from the previous period in words and never a status colour', async () => {
    renderPage();

    const stats = await screen.findByRole('group', { name: 'Headline numbers' });
    const cards = ['Applications', 'Reached interview', 'Offers', 'Hires', 'Median time to hire'].map((title) => {
      const card = within(stats).getByText(title).closest('[data-slot="report-stat"]') as HTMLElement;
      expect(card, title).not.toBeNull();
      return card;
    });
    expect(cards.map((card) => card.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Applications 8 3 more than before',
      'Reached interview 3 38% of applications 1 fewer than before',
      'Offers 1 No change',
      'Hires 3 No change',
      'Median time to hire 21 days 3 days faster',
    ]);
    // The change is muted text with an arrow: a report states how far a number moved, it does not judge it.
    expect(stats.querySelectorAll('[class*="text-status-"]')).toHaveLength(0);
    expect(stats.querySelectorAll('[data-report-change] svg')).toHaveLength(3);
  });

  it('shows the funnel with each stage linked to its candidates and the biggest drop marked', async () => {
    const asked = renderPage();

    const funnel = await screen.findByRole('region', { name: 'Hiring funnel' });
    const rows = within(within(funnel).getByRole('list', { name: 'Hiring funnel' })).getAllByRole('img');
    expect(rows.map((row) => row.getAttribute('aria-label'))).toEqual([
      'Applied: 8 applications, 100% of applicants, 63% moved on',
      'Screening: 5 applications, 63% of applicants, 60% moved on',
      'Interview: 3 applications, 38% of applicants, 33% moved on, the biggest drop',
      'Hired: 1 application, 13% of applicants',
    ]);
    const link = within(funnel).getByRole('link', { name: 'Interview' });
    const href = new URL(link.getAttribute('href')!, 'http://dashboard.test');
    expect(href.pathname).toBe('/candidates');
    expect(href.searchParams.get('reached')).toBe('Interview');
    expect(href.searchParams.get('from')).toBe(asked[0]!.searchParams.get('from'));
    expect(href.searchParams.get('to')).toBe(asked[0]!.searchParams.get('to'));
    // No sentence under the title (W9), and no callout under the funnel.
    expect(within(funnel).queryByText(/biggest drop/)).toBeNull();
  });

  it('shows applications and hires by week, reading the latest week out', async () => {
    renderPage();

    const weekly = await screen.findByRole('region', { name: 'Applications and hires by week' });
    expect(within(weekly).getByRole('list', { name: 'Legend' })).toHaveTextContent(/Applications\s*Hires/);
    const chart = within(weekly).getByRole('group', { name: 'Applications and hires by week' });
    expect(weekly.querySelector('[data-week-readout]')).toHaveTextContent(/Week of (Oct 12|12 Oct): 5 applications, 2 hires/);

    chart.focus();
    await fireEvent.keyDown(chart, { key: 'ArrowLeft' });
    expect(weekly.querySelector('[data-week-readout]')).toHaveTextContent(/Week of (Oct 5|5 Oct): 3 applications, 0 hires/);
  });

  it('shows time to hire by stage and by job, and the overall median only in the headline', async () => {
    renderPage();

    const hires = await screen.findByRole('region', { name: 'Time to hire' });
    expect(within(hires).getAllByRole('img').map((row) => row.getAttribute('aria-label'))).toEqual([
      'Applied: 2 days, 3 hires',
      'Interview: 10 days, 3 hires',
      // The longest median leads.
      `${jobs[1]!.title}: 30 days, 1 hire`,
      `${jobs[0]!.title}: 14 days, 2 hires`,
    ]);
    expect(hires).not.toHaveTextContent('Median across');
    expect(screen.getAllByText('21 days')).toHaveLength(1);
  });

  it('shows the sources with their hire rate, and the open jobs as a table', async () => {
    renderPage();

    const sources = await screen.findByRole('region', { name: 'Sources' });
    expect(within(sources).getAllByRole('img').map((row) => row.getAttribute('aria-label'))).toEqual([
      'Careers page: 6 applications, 1 hire, 17% hire rate',
      'Your website: 2 applications, 0 hires, 0% hire rate',
    ]);

    const open = screen.getByRole('region', { name: 'Open jobs' });
    const table = within(open).getByRole('table');
    expect(within(open).queryByRole('button', { name: 'Chart' })).toBeNull();
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual(['Job', 'Applications', 'In interview', 'Days open', 'Hires']);
    const cells = within(table).getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim()));
    expect(cells).toEqual([[jobs[0]!.title, '6', '2', '12', '1']]);
    expect(within(table).getByRole('link', { name: jobs[0]!.title })).toHaveAttribute('href', `/jobs/${jobs[0]!.id}`);
    await expectNoAxeViolations();
  });

  it('asks for the last 30 days and the previous 30, in the person\'s zone, for every job', async () => {
    const asked = renderPage();
    await screen.findByRole('region', { name: 'Hiring funnel' });

    const { from, to } = rangeInstants({ kind: 'preset', days: 30 }, new Date());
    const previous = previousInstants({ kind: 'preset', days: 30 }, new Date());
    expect(asked[0]!.searchParams.get('from')).toBe(from);
    expect(asked[0]!.searchParams.get('to')).toBe(to);
    expect(asked[0]!.searchParams.get('previousFrom')).toBe(previous.from);
    expect(asked[0]!.searchParams.get('previousTo')).toBe(previous.to);
    expect(asked[0]!.searchParams.get('timeZone')).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone);
    expect(asked[0]!.searchParams.getAll('jobId')).toEqual([]);
    expect(screen.getByRole('button', { name: 'Dates: Last 30 days' })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'Compare with previous period' })).toBeChecked();
  });

  it('compares off: asks for no previous period and shows no change', async () => {
    location.url = new URL('http://dashboard.test/reports?compare=off');
    const asked = renderPage({ ...report, previous: null });
    const stats = await screen.findByRole('group', { name: 'Headline numbers' });

    expect(asked[0]!.searchParams.has('previousFrom')).toBe(false);
    expect(asked[0]!.searchParams.has('previousTo')).toBe(false);
    expect(stats.querySelectorAll('[data-report-change]')).toHaveLength(0);
    expect(screen.getByRole('switch', { name: 'Compare with previous period' })).not.toBeChecked();

    location.url = new URL('http://dashboard.test/reports');
  });

  it('writes the compare switch into the address, leaving it out when on', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Hiring funnel' });

    await fireEvent.click(screen.getByRole('switch', { name: 'Compare with previous period' }));

    expect(goto).toHaveBeenCalledWith(new URL('http://dashboard.test/reports?compare=off'), { replace: true, reset: false });
  });

  it('exports every panel\'s table as one CSV file', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Hiring funnel' });

    await fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }));

    expect(downloadCsv).toHaveBeenCalledOnce();
    const [filename, csv] = vi.mocked(downloadCsv).mock.calls[0]!;
    expect(filename).toMatch(/^screenhire-reports-\d{4}-\d{2}-\d{2}-to-\d{4}-\d{2}-\d{2}\.csv$/);
    const titles = csv.split('\r\n').filter((line, index, lines) => line && (index === 0 || lines[index - 1] === ''));
    expect(titles).toEqual(['Headline numbers', 'Applications and hires by week', 'Hiring funnel', 'Time to hire', 'Sources', 'Open jobs']);
    expect(csv).toContain('Measure,These dates,Previous dates\r\nApplications,8,5\r\n');
    expect(csv).toContain('Stage,Applications,Of applicants,Moved on\r\nApplied,8,1,0.625\r\n');
    expect(csv).toContain('Source,Applications,Hires,Hire rate\r\nCareers page,6,1,0.167\r\nYour website,2,0,0\r\n');
    expect(csv).not.toMatch(/verif/i);
  });

  it('moves the window on when the day changes under an open page', async () => {
    // Only the clock is fake: testing-library's waits run on real timeouts.
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'], now: new Date(2026, 9, 30, 23, 59, 30) });
    try {
      const asked = renderPage();
      await waitFor(() => expect(asked).toHaveLength(1));
      expect(asked[0]!.searchParams.get('to')).toBe(new Date(2026, 9, 31).toISOString());

      vi.advanceTimersByTime(60_000);

      await waitFor(() => expect(asked.at(-1)!.searchParams.get('to')).toBe(new Date(2026, 10, 1).toISOString()));
      // Today is now 31 October, so the last 30 days start on 2 October.
      expect(asked.at(-1)!.searchParams.get('from')).toBe(new Date(2026, 9, 2).toISOString());
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows each chart as a table on request', async () => {
    renderPage();
    const funnel = await screen.findByRole('region', { name: 'Hiring funnel' });

    await fireEvent.click(within(funnel).getByRole('button', { name: 'Table' }));

    const table = within(funnel).getByRole('table');
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual(['Stage', 'Applications', 'Of applicants', 'Moved on']);
    // A funnel's table keeps the pipeline's order until someone sorts it.
    const cells = within(table).getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim()));
    expect(cells).toEqual([
      ['Applied', '8', '100%', '63%'],
      ['Screening', '5', '63%', '60%'],
      ['Interview', '3', '38%', '33%'],
      ['Hired', '1', '13%', 'No later stage'],
    ]);
    expect(within(funnel).getByRole('button', { name: 'Table' })).toHaveAttribute('aria-pressed', 'true');
    await expectNoAxeViolations();

    // Stage sorts by the pipeline's order, not the alphabet: the second click puts Hired first.
    await fireEvent.click(within(table).getByRole('button', { name: 'Stage' }));
    await fireEvent.click(within(table).getByRole('button', { name: 'Stage' }));
    const stages = within(table).getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell')[0]!.textContent?.trim());
    expect(stages).toEqual(['Hired', 'Interview', 'Screening', 'Applied']);
  });

  it('says why a panel is empty when the others have numbers, offering a longer range once', async () => {
    renderPage({ ...report, timeToHire: { stages: [], jobs: [] } });

    const hires = await screen.findByRole('region', { name: 'Time to hire' });
    expect(hires).toHaveTextContent('No hires in these dates');
    expect(within(hires).getByRole('button', { name: 'Show the last 365 days' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Hiring funnel' })).toHaveTextContent('63% moved on');
  });

  it('asks for the jobs and range in the address, and writes a new range into it', async () => {
    location.url = new URL(`http://dashboard.test/reports?range=90&job=${jobs[0]!.id}&job=${jobs[1]!.id}`);
    const asked = renderPage();
    await screen.findByRole('region', { name: 'Hiring funnel' });

    expect(asked[0]!.searchParams.getAll('jobId')).toEqual([jobs[0]!.id, jobs[1]!.id]);
    expect(asked[0]!.searchParams.get('from')).toBe(rangeInstants({ kind: 'preset', days: 90 }, new Date()).from);

    await fireEvent.click(screen.getByRole('button', { name: 'Dates: Last 90 days' }));
    await fireEvent.click(await screen.findByRole('radio', { name: 'Last 365 days' }));

    expect(goto).toHaveBeenCalledWith(new URL(`http://dashboard.test/reports?job=${jobs[0]!.id}&job=${jobs[1]!.id}&range=365`), { replace: true, reset: false });
  });

  it('refuses a custom range that ends before it starts, saying why', async () => {
    renderPage();
    await screen.findByRole('region', { name: 'Hiring funnel' });
    await fireEvent.click(screen.getByRole('button', { name: 'Dates: Last 30 days' }));

    await fireEvent.input(await screen.findByLabelText('From'), { target: { value: '2026-10-20' } });
    await fireEvent.input(screen.getByLabelText('To'), { target: { value: '2026-10-01' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The range has to end on or after its first day.');
    expect(goto).not.toHaveBeenCalled();

    await fireEvent.input(screen.getByLabelText('To'), { target: { value: '2026-10-25' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
    await waitFor(() => expect(goto).toHaveBeenCalledWith(new URL('http://dashboard.test/reports?from=2026-10-20&to=2026-10-25'), { replace: true, reset: false }));
  });

  it('says once that there is nothing in the dates, and offers a longer range', async () => {
    renderPage(nothing);

    const empty = await screen.findByRole('region', { name: 'Reports' });
    expect(empty).toHaveTextContent('Nothing in these dates');
    expect(screen.queryByRole('region', { name: 'Hiring funnel' })).toBeNull();
    expect(screen.getAllByRole('button', { name: 'Show the last 365 days' })).toHaveLength(1);

    await fireEvent.click(within(empty).getByRole('button', { name: 'Show the last 365 days' }));
    expect(goto).toHaveBeenCalledWith(new URL('http://dashboard.test/reports?range=365'), { replace: true, reset: false });
    await expectNoAxeViolations();
  });
});
