import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { page } from '$app/state';
import { apiStub, applications, jobs, now, user, userAs } from '../../test-support/fixtures.js';
import { stubViewport } from '../../test-support/viewport.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/navigation', () => ({ goto: vi.fn() }));
vi.mock('$app/state', () => ({ page: { state: {}, data: {}, url: new URL('http://dashboard.test/'), shallow: null }, navigating: { to: null } }));

const closed = { ...jobs[1]!, id: '01a157d4-0000-7000-8000-000000000003', title: 'Support Engineer', status: 'Closed' as const };

const cellTexts = (row: HTMLElement) => within(row).getAllByRole('cell').map((cell) => cell.textContent?.trim());

function renderPage() {
  return render(WithQueryClient, { props: { component: Page, props: {} } });
}

async function dataRows() {
  const table = await screen.findByRole('table', { name: 'Jobs' });
  const [, ...rows] = within(table).getAllByRole('row');
  return rows;
}

const statusFilter = () => screen.getByRole('group', { name: 'Status' });
const statusToggle = (name: RegExp) => within(statusFilter()).getByRole('button', { name });

async function openRowMenu(title: string) {
  const trigger = await screen.findByRole('button', { name: `Actions for ${title}` });
  // bits-ui opens a menu on pointerdown.
  await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

beforeEach(() => {
  page.data = { user };
});

afterEach(() => {
  vi.unstubAllGlobals();
  // The table remembers its sort in the browser.
  localStorage.clear();
});

describe('jobs page', () => {
  it('shows a row per job, status first, with its location, type, publish date and last update, linking to the job', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs) })));
    renderPage();

    expect((await dataRows()).map(cellTexts)).toEqual([
      // A draft was never published: the cell is blank, with its words for screen readers only (L1).
      // A count that never arrived (nothing here answers the applications request) is blank, and says nothing to a screen
      // reader either: "No candidates" would claim what nobody knows.
      ['Draft', 'Customer Success Lead', 'Manchester, UK', 'Full-time', '', 'Not published', 'Oct 19, 2026', ''],
      ['Open', 'Senior .NET Engineer', 'Remote (GB, IE)', 'Full-time', '', 'Oct 18, 2026', 'Oct 18, 2026', ''],
    ]);
    expect(screen.getByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', `/jobs/${jobs[1]!.id}`);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Jobs');
    expect(screen.getByRole('link', { name: 'New job' })).toHaveAttribute('href', '/jobs/new');
  });

  it('counts who is in play on each job, so a recruiter sees which job needs them, and leaves a closed job or an empty one blank', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([...jobs, closed]), 'GET /v1/applications': () => Response.json([...applications, { ...applications[0]!, jobId: closed.id }]) })),
    );
    renderPage();

    // The fixture's three applications are on the open job and at a stage that is not an end; the one on the closed job is not current.
    const counts = new Map((await dataRows()).map((row) => [cellTexts(row)[1], cellTexts(row)[4]]));
    expect(counts.get('Senior .NET Engineer')).toBe(String(applications.length));
    expect(counts.get('Customer Success Lead')).toBe('No candidates');
    expect(counts.get('Support Engineer')).toBe('No candidates');

    // It sorts, biggest first on a first click.
    await fireEvent.click(within(screen.getByRole('table', { name: 'Jobs' })).getByRole('button', { name: 'Candidates' }));
    expect((await dataRows()).map((row) => cellTexts(row)[1])[0]).toBe('Senior .NET Engineer');
  });

  it('keeps the row menu reachable while a tablet scrolls the table, and the cell around it part of the row link', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs) })));
    renderPage();

    const menu = await screen.findByRole('button', { name: `Actions for ${jobs[0]!.title}` });
    const cell = menu.closest('td')!;
    expect(cell).toHaveClass('sticky', 'right-0', 'pointer-events-none');
    expect(cell.querySelector('span')).toHaveClass('pointer-events-auto');
  });

  it('sorts by every column, opens on Last updated newest first, and remembers the sort (L2)', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([...jobs, closed]) })));
    const { unmount } = renderPage();
    const table = await screen.findByRole('table', { name: 'Jobs' });

    const headers = within(table).getAllByRole('columnheader');
    expect(headers.map((header) => header.textContent?.trim())).toEqual(['Status', 'Title', 'Location', 'Type', 'Candidates', 'Published', 'Last updated', 'Actions']);
    // Every column with a value is a sort button; the actions column is not.
    expect(headers.slice(0, 7).every((header) => within(header).queryByRole('button'))).toBe(true);
    expect(within(headers[7]!).queryByRole('button')).toBeNull();
    expect(within(table).getByRole('columnheader', { name: 'Last updated' })).toHaveAttribute('aria-sort', 'descending');

    await fireEvent.click(within(table).getByRole('button', { name: 'Title' }));
    expect(within(table).getByRole('columnheader', { name: 'Title' })).toHaveAttribute('aria-sort', 'ascending');
    expect((await dataRows()).map((row) => cellTexts(row)[1])).toEqual(['Customer Success Lead', 'Senior .NET Engineer', 'Support Engineer']);

    await fireEvent.click(within(table).getByRole('button', { name: 'Status' }));
    expect((await dataRows()).map((row) => cellTexts(row)[0])).toEqual(['Open', 'Draft', 'Closed']);
    expect(JSON.parse(localStorage.getItem('screenhire:sort:jobs')!)).toEqual({ id: 'status', desc: false });

    unmount();
    renderPage();
    expect(within(await screen.findByRole('table', { name: 'Jobs' })).getByRole('columnheader', { name: 'Status' })).toHaveAttribute('aria-sort', 'ascending');
  });

  it('offers the same sorts in a Sort chip beside the status filter on a phone (L2)', async () => {
    stubViewport(360);
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([...jobs, closed]) })));
    renderPage();

    await screen.findByRole('list', { name: 'Jobs' });
    const sort = screen.getByRole('button', { name: 'Sort' });
    expect(sort.parentElement).toContainElement(statusFilter());

    await fireEvent.pointerDown(sort, { button: 0, pointerType: 'mouse' });
    const choices = (await screen.findAllByRole('menuitemradio')).map((item) => item.textContent?.replace(/,.*$/, '').trim());
    expect(choices).toEqual(['Status', 'Title', 'Location', 'Type', 'Candidates', 'Published', 'Last updated']);
    await fireEvent.click(screen.getByRole('menuitemradio', { name: /^Title/ }));

    await waitFor(() =>
      expect(within(screen.getByRole('list', { name: 'Jobs' })).getAllByRole('listitem').map((item) => within(item).getAllByRole('link')[0]!.textContent?.trim())).toEqual([
        'Customer Success Lead',
        'Senior .NET Engineer',
        'Support Engineer',
      ]),
    );
  });

  it('filters by status and by a search of the title and location, the columns a person sees', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([...jobs, closed]) })));
    renderPage();
    await dataRows();

    await fireEvent.click(statusToggle(/^Open/));
    expect(statusToggle(/^Open/)).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('region', { name: 'Open jobs' })).toBeInTheDocument();
    expect((await dataRows()).map((row) => cellTexts(row)[1])).toEqual(['Senior .NET Engineer']);

    await fireEvent.click(statusToggle(/^All/));
    await fireEvent.input(screen.getByLabelText('Search jobs'), { target: { value: 'manchester' } });
    expect((await dataRows()).map((row) => cellTexts(row)[1])).toEqual(['Customer Success Lead']);

    await fireEvent.input(screen.getByLabelText('Search jobs'), { target: { value: 'nothing like this' } });
    expect(screen.getByText('No jobs match these filters.')).toBeInTheDocument();
    expect(screen.queryByText('No jobs yet')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(await dataRows()).toHaveLength(3);
  });

  it('counts each status in its filter', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([...jobs, closed]) })));
    renderPage();
    await dataRows();

    // Each count, All's included, is a count badge after its label (T4).
    const toggles = within(statusFilter()).getAllByRole('button');
    expect(toggles.map((toggle) => toggle.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['All', 'Open 1', 'Draft 1', 'Closed 1']);
    expect(toggles[0]).toHaveAttribute('aria-pressed', 'true');
  });

  it('says so when there are no jobs', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([]) })));
    renderPage();

    expect(await screen.findByText('No jobs yet')).toBeInTheDocument();
  });

  it('offers no actions on a closed job', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([closed]) })));
    renderPage();

    await dataRows();
    expect(screen.queryByRole('button', { name: /^Actions for/ })).not.toBeInTheDocument();
  });

  it('offers no row menu to someone who may not edit jobs, whose every item needs that', async () => {
    page.data = { user: userAs('Viewer') };
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs) })));
    renderPage();

    await dataRows();
    expect(screen.queryByRole('button', { name: /^Actions for/ })).not.toBeInTheDocument();
  });

  it('shows the failure and a way to try again when the list does not load', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => new Response(null, { status: 503 }) })));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load jobs (HTTP 503).');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('shows each job as a list item on a phone', async () => {
    stubViewport(360);
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs) })));
    renderPage();

    const list = await screen.findByRole('list', { name: 'Jobs' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(2);
    expect(within(list).getByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', `/jobs/${jobs[1]!.id}`);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    // The primary action sits in the action bar, not the header band (section 8).
    const newJob = screen.getByRole('link', { name: 'New job' });
    expect(newJob.closest('[data-slot=action-bar]')).not.toBeNull();
    expect(document.querySelector('[data-slot=header-band] a[href="/jobs/new"]')).toBeNull();
  });

  // A long list is a page at a time on a phone (S9), and a new search starts from the first page again.
  it('lists 50 jobs at a time on a phone and goes back to the first page after a search', async () => {
    stubViewport(360);
    const many = Array.from({ length: 60 }, (_, index) => ({ ...jobs[1]!, id: `job-${index}`, title: `Role ${index}` }));
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(many) })));
    renderPage();

    const items = async () => within(await screen.findByRole('list', { name: 'Jobs' })).getAllByRole('listitem');
    expect(await items()).toHaveLength(50);
    expect(screen.getByText('Showing 50 of 60')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Show more' }));
    expect(await items()).toHaveLength(60);

    await fireEvent.input(screen.getByLabelText('Search jobs'), { target: { value: 'Role' } });
    await fireEvent.input(screen.getByLabelText('Search jobs'), { target: { value: '' } });
    expect(await items()).toHaveLength(50);
  });
});

describe('jobs page actions', () => {
  it('opens Edit details from the row menu as a link, before the action that changes the job (A5)', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs) })));
    renderPage();

    const menu = await openRowMenu('Customer Success Lead');

    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Edit details', 'Publish to every board']);
    expect(within(menu).getByRole('menuitem', { name: 'Edit details' })).toHaveAttribute('href', `/jobs/${jobs[0]!.id}/edit`);
  });

  it('publishes a draft from its row menu and refetches the list', async () => {
    const url = `/v1/jobs/${jobs[0]!.id}/publish`;
    const fetchMock = vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs), [`POST ${url}`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPage();

    const menu = await openRowMenu('Customer Success Lead');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Publish to every board' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(url, expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(fetchMock.mock.calls.filter(([request]) => request === '/v1/jobs')).toHaveLength(2));
  });

  it('closes an open job only after the confirmation', async () => {
    const url = `/v1/jobs/${jobs[1]!.id}/close`;
    const fetchMock = vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs), [`POST ${url}`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPage();

    const menu = await openRowMenu('Senior .NET Engineer');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Close & remove everywhere' }));
    const dialog = await screen.findByRole('dialog', { name: 'Close this job and remove it everywhere?' });
    expect(fetchMock).not.toHaveBeenCalledWith(url, expect.anything());

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Close & remove everywhere' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(url, expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it("says so with the API's reason when publishing is refused", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/jobs': () => Response.json(jobs),
          [`POST /v1/jobs/${jobs[0]!.id}/publish`]: () =>
            Response.json({ title: 'Conflict', detail: 'Adzuna needs a longer description.' }, { status: 409 }),
        }),
      ),
    );
    renderPage();

    const menu = await openRowMenu('Customer Success Lead');
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Publish to every board' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Adzuna needs a longer description.');
  });

  describe('when the plan refuses to publish (HTTP 402)', () => {
    const detail = 'Your plan allows 5 open jobs and 5 are open. Close a job, or ask an owner to upgrade the plan.';
    const publishRefused = async () => {
      vi.stubGlobal(
        'fetch',
        vi.fn(
          apiStub({
            'GET /v1/jobs': () => Response.json(jobs),
            [`POST /v1/jobs/${jobs[0]!.id}/publish`]: () =>
              Response.json({ type: 'urn:screenhire:problem:plan-limit', title: 'Plan limit reached', detail }, { status: 402 }),
          }),
        ),
      );
      renderPage();
      const menu = await openRowMenu('Customer Success Lead');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Publish to every board' }));
      return (await screen.findByText(detail)).closest('[data-slot="notice"]') as HTMLElement;
    };

    it('shows an owner the sentence in a notice that links to Billing', async () => {
      const notice = await publishRefused();

      expect(within(notice).getByRole('link', { name: 'Upgrade plan' })).toHaveAttribute('href', '/settings/billing');
      expect(screen.queryByRole('alert')).toBeNull();
      // A full-width row above the table, not a card in the panel's footer (S4).
      expect(notice.closest('[data-slot="panel"]')).toBeNull();
    });

    it('shows a recruiter the same sentence with no link, since only an owner can upgrade', async () => {
      page.data = { user: userAs('Recruiter') };
      const notice = await publishRefused();

      expect(within(notice).queryByRole('link')).toBeNull();
    });
  });
});

describe('jobs page, overview', () => {
  // Fixed, so "this week" is the same on every machine; the applications below are placed around it.
  const day = 24 * 60 * 60 * 1000;
  const at = (offsetMs: number) => new Date(now + offsetMs).toISOString();
  const rows = [
    { ...applications[0]!, id: 'app-1', submittedAt: at(-day), stage: { id: 'applied', name: 'Applied', kind: 'Applied' as const } },
    { ...applications[0]!, id: 'app-2', submittedAt: at(-3 * day), stage: { id: 'applied', name: 'Applied', kind: 'Applied' as const } },
    { ...applications[0]!, id: 'app-3', submittedAt: at(-20 * day), stage: { id: 'interview', name: 'Interview', kind: 'Interview' as const }, nextInterviewAt: at(2 * day) },
    { ...applications[0]!, id: 'app-4', submittedAt: at(-30 * day), stage: { id: 'interview', name: 'Interview', kind: 'Interview' as const }, nextInterviewAt: at(12 * day) },
  ];
  const billing = { available: true, state: 'Trialing', openJobs: 1, activeJobLimit: 5, canManage: true, currentPlan: null, plans: [] };

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(now);
  });
  afterEach(() => vi.useRealTimers());

  const stubOverview = (extra: Record<string, () => Response> = {}) =>
    vi.stubGlobal(
      'fetch',
      vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs), 'GET /v1/applications': () => Response.json(rows), 'GET /v1/organization/billing': () => Response.json(billing), ...extra })),
    );
  const tile = (title: string) => within(screen.getByRole('group', { name: 'Overview' })).getByRole('link', { name: new RegExp(title) });

  it('replaces the setup checklist with four numbers, each a link to the list it counts', async () => {
    stubOverview();
    renderPage();

    await screen.findByRole('group', { name: 'Overview' });
    await waitFor(() => expect(within(screen.getByRole('group', { name: 'Overview' })).getAllByRole('link')).toHaveLength(4));
    expect(screen.queryByRole('region', { name: 'Set up your organization' })).toBeNull();
    expect(tile('New this week')).toHaveTextContent('2');
    expect(tile('New this week')).toHaveAttribute('href', '/candidates?view=new-this-week');
    expect(tile('Not screened')).toHaveTextContent('2');
    expect(tile('Not screened')).toHaveAttribute('href', '/candidates?view=not-screened');
    // Only the booking inside the week counts; the one twelve days out does not.
    expect(tile('Interviews this week')).toHaveTextContent('1');
    expect(tile('Interviews this week')).toHaveAttribute('href', '/candidates?view=interviews-this-week');
    expect(tile('Open jobs')).toHaveAttribute('href', '/?status=Open');
  });

  it('shows an Owner open jobs against the plan limit, and anyone else the count alone', async () => {
    stubOverview();
    const { unmount } = renderPage();
    await waitFor(() => expect(tile('Open jobs')).toHaveTextContent('1 of 5'));
    unmount();

    page.data = { user: userAs('Recruiter') };
    stubOverview();
    renderPage();
    await waitFor(() => expect(tile('Open jobs')).toHaveTextContent('Open jobs 1'));
    expect(tile('Open jobs')).not.toHaveTextContent('of');
    expect(fetch).not.toHaveBeenCalledWith('/v1/organization/billing', expect.anything());
  });

  it('leaves the application numbers out when they cannot load, rather than showing zero', async () => {
    stubOverview({ 'GET /v1/applications': () => new Response(null, { status: 500 }) });
    renderPage();

    await waitFor(() => expect(within(screen.getByRole('group', { name: 'Overview' })).getAllByRole('link')).toHaveLength(1));
    expect(tile('Open jobs')).toBeInTheDocument();
  });

  it('opens the list on the status in the address, so the Open jobs tile lands filtered', async () => {
    page.url = new URL('http://dashboard.test/?status=Open');
    stubOverview();
    renderPage();

    await dataRows();
    expect(within(statusFilter()).getByRole('button', { name: /^Open/ })).toHaveAttribute('aria-pressed', 'true');
    expect(await screen.findByRole('region', { name: 'Open jobs' })).toBeInTheDocument();
    page.url = new URL('http://dashboard.test/');
  });
});
