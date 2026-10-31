import type { ApplicationSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { WorkflowStage } from '@screenhire/api-client';
import { applications, apiStub, jobId, stages } from '../../test-support/fixtures.js';
import { summary } from '../../test-support/verification-fixtures.js';
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
        const stage = withEnds.find((candidate) => candidate.id === toStageId)!;
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

function renderBoard(boardStages: WorkflowStage[] = stages) {
  return render(WithQueryClient, { props: { component: PipelineHarness, props: { jobId, stages: boardStages } } });
}

const withEnds: WorkflowStage[] = [
  ...stages,
  { id: 'hired', name: 'Hired', kind: 'Hired' },
  { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
  { id: 'withdrawn', name: 'Withdrawn', kind: 'Withdrawn' },
];

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
    // The time it arrived, never strung together with other facts by a dot (W3).
    expect(card('Casey Morgan').querySelector('time')).toHaveAttribute('datetime', casey.submittedAt);
    expect(card('Casey Morgan')).not.toHaveTextContent('·');
  });

  it('gives a stage with candidates a 296px column and shrinks an empty one to 188px (section 5)', async () => {
    stubApi();
    renderBoard();

    await screen.findByRole('list', { name: 'Applied stage' });
    const width = (id: string) => document.querySelector(`[data-stage-column="${id}"]`)!.className;
    expect(width('applied')).toContain('w-74');
    expect(width('screening')).toContain('w-74');
    expect(width('interview')).toContain('w-47');
    expect(width('offer')).toContain('w-47');
  });

  it('counts the stages that end a candidacy in one 188px Closed column, without drawing their cards', async () => {
    const rows = [applications[0]!, { ...applications[1]!, stage: { id: 'hired', name: 'Hired', kind: 'Hired' as const } }, { ...applications[2]!, stage: { id: 'rejected', name: 'Rejected', kind: 'Rejected' as const } }];
    stubApi({ rows });
    renderBoard(withEnds);

    await screen.findByRole('list', { name: 'Applied stage' });
    const closed = screen.getByRole('region', { name: /^Closed/ });
    expect(closed.className).toContain('w-47');
    expect(within(closed).getByRole('heading').textContent?.replace(/\s+/g, ' ').trim()).toBe('Closed 2');
    expect(within(closed).getAllByText(/^(Hired|Rejected|Withdrawn)$/).map((name) => `${name.textContent} ${name.closest('[data-closed-stage]')!.textContent?.replace(/\D/g, '')}`)).toEqual([
      'Hired 1',
      'Rejected 1',
      'Withdrawn 0',
    ]);
    // Each is a drop target for a card leaving the pipeline.
    expect(within(closed).getByRole('list', { name: 'Rejected stage' })).toBeInTheDocument();
    expect(screen.queryByRole('listitem', { name: /^Jordan Lee,/ })).toBeNull();
  });

  it('draws a candidate kept in mind in no stage column, and counts them in Closed (docs/architecture/talent-pool.md)', async () => {
    // Jordan Lee was in Screening when the team kept them in mind: the stage is where they stood, not where they are.
    const rows = [applications[0]!, { ...applications[1]!, keptInMindAt: '2026-10-20T11:00:00Z' }, applications[2]!];
    stubApi({ rows });
    renderBoard(withEnds);

    await screen.findByRole('list', { name: 'Applied stage' });
    expect(screen.queryByRole('listitem', { name: /^Jordan Lee,/ })).toBeNull();
    expect(screen.getByRole('heading', { name: /^Screening/ }).textContent?.replace(/\s+/g, ' ').trim()).toBe('Screening 0');
    expect(screen.getByRole('heading', { name: /^Applied/ }).textContent?.replace(/\s+/g, ' ').trim()).toBe('Applied 2');
    const closed = screen.getByRole('region', { name: /^Closed/ });
    expect(within(closed).getByRole('heading').textContent?.replace(/\s+/g, ' ').trim()).toBe('Closed 1');
  });

  it('adds a kept candidate to Closed beside those who were hired or rejected, in a row of their own so the rows add up', async () => {
    const rows = [
      { ...applications[0]!, stage: { id: 'hired', name: 'Hired', kind: 'Hired' as const } },
      { ...applications[1]!, keptInMindAt: '2026-10-20T11:00:00Z' },
      applications[2]!,
    ];
    stubApi({ rows });
    renderBoard(withEnds);

    await screen.findByRole('list', { name: 'Applied stage' });
    const closed = screen.getByRole('region', { name: /^Closed/ });
    expect(within(closed).getByRole('heading').textContent?.replace(/\s+/g, ' ').trim()).toBe('Closed 2');
    expect(closed.querySelector('[data-kept-in-mind]')).toHaveTextContent(/^Kept in mind\s*1$/);
  });

  it('says nothing on a calm card, and shows only what needs a look (section 21)', async () => {
    const rows = [
      { ...applications[0]!, verification: summary() },
      { ...applications[2]!, verification: summary({ authenticity: 'VerificationRecommended', authenticityReasons: ['A'], aiAssistance: 'SomeIndicators', aiAssistanceReasons: ['B'] }) },
    ];
    stubApi({ rows });
    renderBoard();

    await screen.findByRole('list', { name: 'Applied stage' });
    expect(card('Casey Morgan')).not.toHaveTextContent(/No concerns|No signs/);
    expect(card('Casey Morgan').querySelector('[data-quiet], [data-status]')).toBeNull();
    expect(within(card('Sam Taylor')).getByText('Verification suggested')).toBeInTheDocument();
    expect(within(card('Sam Taylor')).getByText('Some signs')).toBeInTheDocument();
  });

  it('moves a card into a closed stage from the keyboard, and focus follows to that stage', async () => {
    const fetchMock = stubApi({ rows: [{ ...applications[0]!, stage: { id: 'offer', name: 'Offer', kind: 'Offer' } }] });
    renderBoard(withEnds);

    await screen.findByRole('list', { name: 'Offer stage' });
    const moving = card('Casey Morgan');
    moving.focus();
    await fireEvent.keyDown(moving, { key: 'ArrowRight', shiftKey: true });

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining(`/v1/applications/${casey.id}/move`), expect.objectContaining({ method: 'POST' })));
    await waitFor(() => expect(document.activeElement).toHaveAttribute('data-closed-stage', 'hired'));
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
    expect(badge.querySelector('[data-status-icon="clock-alert"]')).not.toBeNull();
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
