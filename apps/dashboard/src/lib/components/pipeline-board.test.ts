import type { ApplicationSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { applications, apiStub, jobId, stages } from '../../test-support/fixtures.js';
import PipelineHarness from '../../test-support/pipeline-harness.svelte';
import WithQueryClient from '../../test-support/with-query-client.svelte';

const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock('$app/navigation', () => navigation);

const casey = applications[0]!;

/** The API as the board sees it: the job's applications, and moves that change them. */
function stubApi(options: { refuseMove?: boolean; rows?: ApplicationSummary[] } = {}) {
  let current: ApplicationSummary[] = (options.rows ?? applications).map((application) => ({ ...application }));
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/jobs/${jobId}/applications`]: () => Response.json(current),
      [`POST /v1/applications/${casey.id}/move`]: (init) => {
        if (options.refuseMove) {
          return Response.json({ detail: 'Someone else moved this application at the same moment; refresh and try again.' }, { status: 409 });
        }
        const { toStageId } = JSON.parse(String(init?.body)) as { toStageId: string };
        const stage = stages.find((candidate) => candidate.id === toStageId)!;
        current = current.map((application) =>
          application.id === casey.id ? { ...application, stage: { id: stage.id, name: stage.name, kind: stage.kind } } : application,
        );
        return Response.json({});
      },
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderBoard() {
  return render(WithQueryClient, { props: { component: PipelineHarness, props: { jobId, stages } } });
}

const column = (name: string) => screen.getByRole('list', { name: `${name} stage` });
const card = (name: string) => screen.getByRole('listitem', { name: new RegExp(`^${name},`) });

afterEach(() => {
  vi.unstubAllGlobals();
  navigation.goto.mockClear();
});

describe('pipeline board', () => {
  it('shows a column per stage with its count and cards', async () => {
    stubApi();
    renderBoard();

    await screen.findByRole('list', { name: 'Applied stage' });
    expect(stages.map((stage) => screen.getByRole('heading', { name: new RegExp(`^${stage.name}`) }).textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Applied 2',
      'Screening 1',
      'Interview 0',
      'Offer 0',
    ]);
    expect(within(column('Applied')).getAllByRole('listitem').map((item) => item.getAttribute('aria-label'))).toEqual([
      'Casey Morgan, Applied',
      'Sam Taylor, Applied',
    ]);
    // Facts as a pill and a time, never strung together with a dot (W3).
    expect(within(card('Casey Morgan')).getByText('Careers page')).toHaveAttribute('data-slot', 'badge');
    expect(card('Casey Morgan').querySelector('time')).toHaveAttribute('datetime', casey.submittedAt);
    expect(card('Casey Morgan')).not.toHaveTextContent('·');
  });

  // Overdue is the team's status (design system section 3.2), first in the card's status row.
  it('shows an overdue card with the review badge first, and nothing for a card within its time limit', async () => {
    const twoDaysAgo = new Date(Date.now() - 2 * 86_400_000 - 60_000).toISOString();
    const inADay = new Date(Date.now() + 86_400_000).toISOString();
    stubApi({ rows: [{ ...applications[0]!, slaDueAt: twoDaysAgo }, { ...applications[1]!, slaDueAt: inADay }, applications[2]!] });
    renderBoard();

    await screen.findByRole('list', { name: 'Applied stage' });
    const badge = within(card('Casey Morgan')).getByText('Overdue by 2 days');
    expect(badge).toHaveAttribute('data-status', 'review');
    expect(badge.querySelector('[data-shape="diamond"]')).not.toBeNull();
    expect(card('Casey Morgan').querySelector('[data-status]')).toBe(badge);
    expect(card('Jordan Lee')).not.toHaveTextContent('Overdue');
    expect(card('Sam Taylor')).not.toHaveTextContent('Overdue');
  });

  it('moves the focused card to the next stage with Shift + Right, keeps focus on it and announces it', async () => {
    const fetchMock = stubApi();
    renderBoard();
    await screen.findByRole('list', { name: 'Applied stage' });
    card('Casey Morgan').focus();

    await fireEvent.keyDown(card('Casey Morgan'), { key: 'ArrowRight', shiftKey: true });

    await waitFor(() => expect(within(column('Screening')).getByRole('listitem', { name: 'Casey Morgan, Screening' })).toBeInTheDocument());
    expect(fetchMock).toHaveBeenCalledWith(
      `/v1/applications/${casey.id}/move`,
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ toStageId: 'screening' }) }),
    );
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Moved Casey Morgan to Screening.'));
    await waitFor(() => expect(document.activeElement).toBe(card('Casey Morgan')));
  });

  it('moves back a stage with Shift + Left, and goes nowhere from the first stage', async () => {
    const fetchMock = stubApi();
    renderBoard();
    await screen.findByRole('list', { name: 'Applied stage' });

    await fireEvent.keyDown(card('Casey Morgan'), { key: 'ArrowLeft', shiftKey: true });

    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/move'))).toBe(false);
    expect(within(column('Applied')).getByRole('listitem', { name: 'Casey Morgan, Applied' })).toBeInTheDocument();
  });

  it('puts the card back and says why when the API refuses the move', async () => {
    stubApi({ refuseMove: true });
    renderBoard();
    await screen.findByRole('list', { name: 'Applied stage' });

    await fireEvent.keyDown(card('Casey Morgan'), { key: 'ArrowRight', shiftKey: true });

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Someone else moved this application at the same moment'));
    await waitFor(() => expect(within(column('Applied')).getByRole('listitem', { name: 'Casey Morgan, Applied' })).toBeInTheDocument());
  });

  it('opens the application with Enter on a card', async () => {
    stubApi();
    renderBoard();
    await screen.findByRole('list', { name: 'Applied stage' });

    await fireEvent.keyDown(card('Sam Taylor'), { key: 'Enter' });

    expect(navigation.goto).toHaveBeenCalledWith(`/applications/${applications[2]!.id}`);
  });

  it('offers a "Move" menu on every card, the non-drag alternative', async () => {
    stubApi();
    renderBoard();
    await screen.findByRole('list', { name: 'Applied stage' });

    expect(screen.getAllByRole('button', { name: /^Move .* to another stage$/ })).toHaveLength(applications.length);
  });
});
