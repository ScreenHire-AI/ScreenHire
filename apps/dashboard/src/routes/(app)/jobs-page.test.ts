import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobs } from '../../test-support/fixtures.js';
import { stubViewport } from '../../test-support/viewport.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

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

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('jobs page', () => {
  it('shows a row per job, status first, with its location, type, publish date and last update, linking to the job', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json(jobs) })));
    renderPage();

    expect((await dataRows()).map(cellTexts)).toEqual([
      ['Draft', 'Customer Success Lead', 'Manchester, UK', 'Full-time', '-', 'Oct 19, 2026', ''],
      ['Open', 'Senior .NET Engineer', 'Remote (GB, IE)', 'Full-time', 'Oct 18, 2026', 'Oct 18, 2026', ''],
    ]);
    expect(screen.getByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', `/jobs/${jobs[1]!.id}`);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Jobs');
    expect(screen.getByRole('link', { name: 'New job' })).toHaveAttribute('href', '/jobs/new');
  });

  it('filters by status and by a search of title, department and location', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([...jobs, closed]) })));
    renderPage();
    await dataRows();

    await fireEvent.click(statusToggle(/^Open/));
    expect(statusToggle(/^Open/)).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('region', { name: 'Open jobs' })).toBeInTheDocument();
    expect((await dataRows()).map((row) => cellTexts(row)[1])).toEqual(['Senior .NET Engineer']);

    await fireEvent.click(statusToggle(/^All$/));
    await fireEvent.input(screen.getByLabelText('Search jobs'), { target: { value: 'manchester' } });
    expect((await dataRows()).map((row) => cellTexts(row)[1])).toEqual(['Customer Success Lead']);

    await fireEvent.input(screen.getByLabelText('Search jobs'), { target: { value: 'nothing like this' } });
    expect(screen.getByText('No jobs match')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(await dataRows()).toHaveLength(3);
  });

  it('counts each status in its filter', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/jobs': () => Response.json([...jobs, closed]) })));
    renderPage();
    await dataRows();

    // Each count is a count pill after its label (T4).
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
});

describe('jobs page actions', () => {
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
});
