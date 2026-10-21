import type { JobDetail } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, board, jobId, now, posting } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import DistributionStrip from './distribution-strip.svelte';

const boards = [
  board('careers', 'Careers page', { mode: 'Hosted', canUnpublishPerBoard: false }),
  board('adzuna', 'Adzuna feed', { mode: 'Pull', confirmsLive: false, confirmsRemoval: false }),
  board('sandbox', 'Sandbox board'),
];

type StripJob = Pick<JobDetail, 'id' | 'status' | 'postings'>;

function renderStrip(job: StripJob) {
  return render(WithQueryClient, { props: { component: DistributionStrip, props: { job, boards, now } } });
}

const item = (boardId: string) => document.querySelector<HTMLElement>(`[data-board="${boardId}"]`)!;
const stateOf = (boardId: string) => item(boardId).querySelector('[data-state]')?.textContent;
const in20Seconds = new Date(now + 20_000).toISOString();
const in4Hours = new Date(now + 4 * 3_600_000).toISOString();

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('distribution strip', () => {
  it("shows each board's state with its shape mark, and the board's own estimate", () => {
    renderStrip({
      id: jobId,
      status: 'Open',
      postings: [
        posting('careers', 'Careers page'),
        posting('adzuna', 'Adzuna feed', { state: 'Submitted', confirmsLive: false, confirmsRemoval: false, expectedBy: in4Hours }),
        posting('sandbox', 'Sandbox board', { state: 'Queued', expectedBy: in20Seconds }),
      ],
    });

    expect(stateOf('careers')).toBe('Live');
    expect(stateOf('adzuna')).toBe('Sent · ~4h');
    expect(stateOf('sandbox')).toBe('Queued · ~20s');
    expect(item('careers').querySelector('[data-shape]')).toHaveAttribute('data-shape', 'circle');
    expect(item('sandbox').querySelector('[data-shape]')).toHaveAttribute('data-shape', 'diamond');
    expect(item('adzuna')).toHaveTextContent('this board never confirms when a job shows');
  });

  it('never says "removed" for a board that does not confirm removal', () => {
    renderStrip({
      id: jobId,
      status: 'Closed',
      postings: [
        posting('careers', 'Careers page', { state: 'Closed' }),
        posting('adzuna', 'Adzuna feed', { state: 'Closing', confirmsLive: false, confirmsRemoval: false, expectedBy: in4Hours }),
        posting('sandbox', 'Sandbox board', { state: 'Closing', expectedBy: in20Seconds }),
      ],
    });

    expect(stateOf('careers')).toBe('Removed');
    expect(stateOf('adzuna')).toBe('Removal requested · ~4h');
    expect(stateOf('sandbox')).toBe('Removing · ~20s');
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('publishes a draft to every board in one click', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/publish`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderStrip({ id: jobId, status: 'Draft', postings: [] });
    expect(stateOf('sandbox')).toBe('Not posted');

    await fireEvent.click(screen.getByRole('button', { name: 'Publish to 3 boards' }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/v1/jobs/${jobId}/publish`, expect.objectContaining({ method: 'POST' })));
    expect(fetchMock.mock.calls[0]![1]!.body).toBeUndefined();
  });

  it('takes the job down from one board, and offers no take-down where only closing the job removes it', async () => {
    const fetchMock = vi.fn(apiStub({ [`POST /v1/jobs/${jobId}/postings/sandbox/close`]: () => Response.json({}) }));
    vi.stubGlobal('fetch', fetchMock);
    renderStrip({
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
    renderStrip({
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
    renderStrip({ id: jobId, status: 'Open', postings: [posting('sandbox', 'Sandbox board')] });

    await fireEvent.click(screen.getByRole('button', { name: 'Take down from Sandbox board' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('A board answered meanwhile; refresh and try again.'));
  });
});
