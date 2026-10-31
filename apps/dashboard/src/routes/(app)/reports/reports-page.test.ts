import type { HiringReport } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { goto } from '$app/navigation';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { rangeInstants } from '#lib/report-range.js';
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

const report: HiringReport = {
  from: '2026-10-01T00:00:00Z',
  to: '2026-10-31T00:00:00Z',
  funnel: [
    { kind: 'Applied', applications: 8, movedOn: 5 },
    { kind: 'Screening', applications: 5, movedOn: 3 },
    { kind: 'Interview', applications: 3, movedOn: 1 },
    { kind: 'Hired', applications: 1, movedOn: null },
  ],
  timeToHire: {
    medianDays: 21.4,
    hires: 3,
    jobs: [
      { jobId: jobs[0]!.id, jobTitle: jobs[0]!.title, hires: 2, medianDays: 14 },
      { jobId: jobs[1]!.id, jobTitle: jobs[1]!.title, hires: 1, medianDays: 30 },
    ],
  },
  sources: [
    { source: 'CareersSite', applications: 6, hires: 1 },
    { source: 'Recruiter', applications: 2, hires: 0 },
  ],
};

const nothing: HiringReport = { ...report, funnel: [], timeToHire: { medianDays: null, hires: 0, jobs: [] }, sources: [] };

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
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('reports page', () => {
  it('shows the funnel, time to hire and sources for the last 30 days of every job', async () => {
    const asked = renderPage();

    const funnel = await screen.findByRole('region', { name: 'Hiring funnel' });
    const rows = within(within(funnel).getByRole('list', { name: 'Hiring funnel' })).getAllByRole('img');
    expect(rows.map((row) => row.getAttribute('aria-label'))).toEqual([
      'Applied: 8 applications, 63% moved on',
      'Screening: 5 applications, 60% moved on',
      'Interview: 3 applications, 33% moved on',
      'Hired: 1 application',
    ]);
    expect(funnel).toHaveTextContent('63% moved on');

    const hires = screen.getByRole('region', { name: 'Time to hire' });
    expect(hires).toHaveTextContent('21 days');
    expect(hires).toHaveTextContent('Median across 3 hires');
    // The longest median leads.
    expect(within(hires).getAllByRole('img').map((row) => row.getAttribute('aria-label'))).toEqual([
      `${jobs[1]!.title}: 30 days, 1 hire`,
      `${jobs[0]!.title}: 14 days, 2 hires`,
    ]);

    const sources = screen.getByRole('region', { name: 'Sources' });
    expect(within(sources).getByRole('list', { name: 'Legend' })).toHaveTextContent(/Applications\s*Hires/);
    expect(within(sources).getAllByRole('img').map((row) => row.getAttribute('aria-label'))).toEqual([
      'Careers page: 6 applications, 1 hire',
      'Added by a recruiter: 2 applications, 0 hires',
    ]);

    const { from, to } = rangeInstants({ kind: 'preset', days: 30 }, new Date());
    expect(asked[0]!.searchParams.get('from')).toBe(from);
    expect(asked[0]!.searchParams.get('to')).toBe(to);
    expect(asked[0]!.searchParams.getAll('jobId')).toEqual([]);
    expect(screen.getByRole('button', { name: 'Dates: Last 30 days' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('shows each chart as a table on request', async () => {
    renderPage();
    const funnel = await screen.findByRole('region', { name: 'Hiring funnel' });

    await fireEvent.click(within(funnel).getByRole('button', { name: 'Table' }));

    const table = within(funnel).getByRole('table');
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual(['Stage', 'Applications', 'Moved on']);
    // A funnel's table keeps the pipeline's order until someone sorts it.
    const cells = within(table).getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim()));
    expect(cells).toEqual([
      ['Applied', '8', '63%'],
      ['Screening', '5', '60%'],
      ['Interview', '3', '33%'],
      ['Hired', '1', 'No later stage'],
    ]);
    expect(within(funnel).getByRole('button', { name: 'Table' })).toHaveAttribute('aria-pressed', 'true');
    await expectNoAxeViolations();
  });

  it('says why a panel is empty when the others have numbers, offering a longer range once', async () => {
    renderPage({ ...report, timeToHire: { medianDays: null, hires: 0, jobs: [] } });

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
