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
const etaOf = (boardId: string) => item(boardId).querySelector('[data-slot=badge]:not([data-status])')?.textContent?.trim();
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
    expect(screen.getByRole('region', { name: 'Distribution' })).toBeInTheDocument();
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
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('offers no board action on a draft, which is published from the page header', () => {
    renderPanel({ id: jobId, status: 'Draft', postings: [] });

    expect(stateOf('sandbox')).toBe('Not posted');
    expect(screen.queryByRole('button', { name: /Post|Publish/ })).not.toBeInTheDocument();
  });

  it("says what the page reports about the boards, such as a saved edit or a failed publish", () => {
    renderPanel({ id: jobId, status: 'Open', postings: [] }, { notice: 'Saved.', publishFailure: 'Could not publish the job (HTTP 409).' });

    expect(screen.getByRole('status')).toHaveTextContent('Saved.');
    expect(screen.getByRole('alert')).toHaveTextContent('Could not publish the job (HTTP 409).');
  });

  it('takes the job down from one board, and offers no take-down where only closing the job removes it', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/postings/sandbox/close`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board')],
    });
    expect(within(item('careers')).queryByRole('button')).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Take down from Sandbox board' }));

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(`/v1/jobs/${jobId}/postings/sandbox/close`, expect.objectContaining({ method: 'POST' })),
    );
  });

  it('posts an open job to a board it is not on, naming only that board', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderPanel({
      id: jobId,
      status: 'Open',
      postings: [posting('careers', 'Careers page'), posting('sandbox', 'Sandbox board', { state: 'Closed' })],
    });

    await fireEvent.click(screen.getByRole('button', { name: 'Post to Sandbox board' }));

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

    await fireEvent.click(screen.getByRole('button', { name: 'Take down from Sandbox board' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('A board answered meanwhile; refresh and try again.'));
  });
});
