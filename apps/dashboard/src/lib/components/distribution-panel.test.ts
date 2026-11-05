import type { JobDetail } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, board, jobId, now, posting } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import DistributionPanel from './distribution-panel.svelte';

const boards = [
  board('careers', 'Careers page', { mode: 'Hosted', canUnpublishPerBoard: false }),
  board('adzuna', 'Adzuna feed', { mode: 'Pull', confirmsLive: false, confirmsRemoval: false }),
  board('sandbox', 'Sandbox board'),
];

type PanelJob = Pick<JobDetail, 'id' | 'status' | 'postings'>;

function renderPanel(job: PanelJob, props: Record<string, unknown> = {}) {
  return render(WithQueryClient, { props: { component: DistributionPanel, props: { job, boards, now, ...props } } });
}

const item = (boardId: string) => document.querySelector<HTMLElement>(`[data-board="${boardId}"]`)!;
const stateOf = (boardId: string) => item(boardId).querySelector('[data-posting-state]')?.textContent?.trim();
const etaOf = (boardId: string) => item(boardId).querySelector('[data-posting-eta]')?.textContent?.trim();
const cellsOf = (boardId: string) => [...item(boardId).querySelectorAll('tr:first-child td')].map((cell) => cell.textContent?.trim());
const menuFor = (boardId: string) => within(item(boardId)).queryByRole('button', { name: /^Actions for / });
async function openMenu(name: string) {
  // bits-ui opens a menu on pointerdown.
  await fireEvent.pointerDown(screen.getByRole('button', { name: `Actions for ${name}` }), { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}
const in20Seconds = new Date(now + 20_000).toISOString();
const in4Hours = new Date(now + 4 * 3_600_000).toISOString();

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('distribution panel', () => {
  it("shows each board's state as a status badge, and the board's own estimate as a pill", () => {
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [
        posting('careers', 'Careers page'),
        posting('adzuna', 'Adzuna feed', { state: 'Submitted', confirmsLive: false, confirmsRemoval: false, expectedBy: in4Hours }),
        posting('sandbox', 'Sandbox board', { state: 'Queued', expectedBy: in20Seconds }),
      ],
    });

    expect(stateOf('careers')).toBe('Live');
    expect(stateOf('adzuna')).toBe('Sent');
    expect(etaOf('adzuna')).toBe('~4h');
    expect(stateOf('sandbox')).toBe('Queued');
    expect(etaOf('sandbox')).toBe('~20s');
    expect(etaOf('careers')).toBeUndefined();
    expect(item('careers').querySelector('[data-status-icon]')).toHaveAttribute('data-status-icon', 'door-open');
    // Waiting on the board is grey: nobody needs to act (T3).
    expect(item('sandbox').querySelector('[data-status-icon]')).toHaveAttribute('data-status-icon', 'clock-4');
    expect(item('sandbox').querySelector('[data-status]')).toHaveAttribute('data-status', 'neutral');
    expect(screen.getByRole('region', { name: 'Job postings' })).toBeInTheDocument();
    expect(item('adzuna')).toHaveTextContent('this board never confirms when a job shows');
  });

  it('never says "removed" for a board that does not confirm removal', () => {
    renderPanel({
      id: jobId,
      status: 'Closed',
      postings: [
        posting('careers', 'Careers page', { state: 'Closed' }),
        posting('adzuna', 'Adzuna feed', { state: 'Closing', confirmsLive: false, confirmsRemoval: false, expectedBy: in4Hours }),
        posting('sandbox', 'Sandbox board', { state: 'Closing', expectedBy: in20Seconds }),
      ],
    });

    expect(stateOf('careers')).toBe('Removed');
    expect(stateOf('adzuna')).toBe('Removal requested');
    expect(stateOf('sandbox')).toBe('Removing');
    expect(screen.queryByRole('button', { name: /^Actions for / })).not.toBeInTheDocument();
  });

  it('offers no board action on a draft, which is published from the page header', () => {
    renderPanel({ id: jobId, status: 'Draft', postings: [] });

    expect(stateOf('sandbox')).toBe('Not posted');
    expect(menuFor('sandbox')).toBeNull();
  });

  it('says when each posting went out and when it last changed, and leaves a board it never reached blank (L1)', () => {
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [
        posting('careers', 'Careers page', { submittedAt: null, liveAt: '2026-11-02T09:00:00Z', updatedAt: '2026-11-03T09:00:00Z' }),
        posting('adzuna', 'Adzuna feed', { state: 'Submitted', submittedAt: '2026-11-01T09:00:00Z', updatedAt: '2026-11-04T09:00:00Z' }),
      ],
    });

    expect(cellsOf('careers').slice(2, 4)).toEqual(['Nov 2, 2026', 'Nov 3, 2026']);
    expect(cellsOf('adzuna').slice(2, 4)).toEqual(['Nov 1, 2026', 'Nov 4, 2026']);
    expect(cellsOf('sandbox').slice(2, 4)).toEqual(['', '']);
    for (const name of ['Status', 'Board', 'Posted', 'Last updated']) {
      expect(screen.getByRole('columnheader', { name })).toBeInTheDocument();
    }
  });

  it('says in words what is wrong with a posting that needs fixes, and where its issues are fixed (E2)', () => {
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [
        posting('careers', 'Careers page', { state: 'Draft', issues: ['Adzuna needs a description of at least 100 characters.'] }),
        posting('sandbox', 'Sandbox board', { state: 'Rejected', lastError: 'Title too long.' }),
      ],
    });

    expect(item('careers').querySelector('[data-posting-problem]')).toHaveTextContent('Not sent: Adzuna needs a description of at least 100 characters.');
    expect(within(item('careers')).getByRole('link', { name: 'Edit details' })).toHaveAttribute('href', `/jobs/${jobId}/edit`);
    expect(item('sandbox').querySelector('[data-posting-problem]')).toHaveTextContent('Rejected by the board: Title too long.');
    expect(within(item('sandbox')).queryByRole('link', { name: 'Edit details' })).toBeNull();
  });

  it('links to the live posting, and copies its link, where the board has one', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', Object.assign(Object.create(navigator), { clipboard: { writeText } }));
    const url = 'https://careers.example.com/acme/jobs/senior-engineer';
    renderPanel({ id: jobId, status: 'Open', postings: [posting('careers', 'Careers page', { viewUrl: url }), posting('sandbox', 'Sandbox board')] });

    const menu = await openMenu('Careers page');
    const view = within(menu).getByRole('menuitem', { name: /^View posting/ });
    expect(view).toHaveAttribute('href', url);
    expect(view).toHaveAttribute('target', '_blank');
    // The reason a board that only closes with the job has no Take down stays beside the link.
    expect(within(menu).getByRole('menuitem', { name: 'Stays on while the job is open' })).toHaveAttribute('aria-disabled', 'true');

    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Copy link' }));

    await waitFor(() => expect(within(menu).getByRole('menuitem', { name: 'Copied' })).toBeInTheDocument());
    expect(writeText).toHaveBeenCalledWith(url);
  });

  it('offers no link for a board that never says where its listing landed', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board')] });

    const menu = await openMenu('Sandbox board');
    expect(within(menu).queryByRole('menuitem', { name: /^View posting/ })).toBeNull();
    expect(within(menu).queryByRole('menuitem', { name: 'Copy link' })).toBeNull();
    expect(within(menu).getByRole('menuitem', { name: 'Take down' })).toBeInTheDocument();
  });

  it('sends a posting whose checks failed to its fix, then offers Retry', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board', { state: 'Draft', issues: ['Sandbox board needs a city.'] })] });

    const menu = await openMenu('Sandbox board');
    expect(within(menu).getByRole('menuitem', { name: 'Edit details' })).toHaveAttribute('href', `/jobs/${jobId}/edit`);
    expect(within(menu).getByRole('menuitem', { name: 'Retry' })).toBeInTheDocument();
  });

  it('sends a posting that needs fixes again from its menu', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board', { state: 'Error', lastError: 'Timed out.' })] });

    await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Retry' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        `/v1/jobs/${jobId}/publish`,
        expect.objectContaining({ method: 'POST', body: JSON.stringify({ boards: ['sandbox'] }) }),
      ),
    );
  });

  it('takes the job down from one board from its menu', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/postings/sandbox/close`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPanel({ id: jobId, status: 'Open', postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board')] });

    await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Take down' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(`/v1/jobs/${jobId}/postings/sandbox/close`, expect.objectContaining({ method: 'POST' })),
    );
  });

  it('says why a board that only closes with the job has nothing to do (E3)', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('careers', 'Careers page')] });

    const item = within(await openMenu('Careers page')).getByRole('menuitem', { name: 'Stays on while the job is open' });
    expect(item).toHaveAttribute('aria-disabled', 'true');
  });

  it('has no menu for a board still removing the job', () => {
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board', { state: 'Closing', expectedBy: in20Seconds })] });

    expect(menuFor('sandbox')).toBeNull();
  });

  it('offers Post for a board the job never went to', async () => {
    renderPanel({ id: jobId, status: 'Open', postings: [] });

    expect(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Post' })).toBeInTheDocument();
  });

  it('posts an open job again to a board it was taken down from, naming only that board', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board', { state: 'Closed' })],
    });

    await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Post again' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        `/v1/jobs/${jobId}/publish`,
        expect.objectContaining({ method: 'POST', body: JSON.stringify({ boards: ['sandbox'] }) }),
      ),
    );
  });

  it("shows the API's reason when a board action is refused", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          [`POST /v1/jobs/${jobId}/postings/sandbox/close`]: () =>
            Response.json({ detail: 'A board answered meanwhile; refresh and try again.' }, { status: 409 }),
        }),
      ),
    );
    renderPanel({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board')] });

    await fireEvent.click(within(await openMenu('Sandbox board')).getByRole('menuitem', { name: 'Take down' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('A board answered meanwhile; refresh and try again.'));
  });
});
