import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { applications, apiStub, jobId, stages } from '../test-support/fixtures.js';
import MovesHarness from '../test-support/moves-harness.svelte';
import WithQueryClient from '../test-support/with-query-client.svelte';

const casey = applications[0]!;
const jordan = applications[1]!;
const sam = applications[2]!;
const screening = stages[1]!;

function stubApi(refuse: ReadonlySet<string>) {
  const routes: Parameters<typeof apiStub>[0] = {
    [`GET /v1/jobs/${jobId}/applications`]: () => Response.json(applications),
    'GET /v1/applications': () => Response.json(applications),
    'GET /v1/candidates': () => Response.json([]),
  };
  for (const application of applications) {
    routes[`POST /v1/applications/${application.id}/move`] = () =>
      refuse.has(application.id)
        ? Response.json({ detail: 'This application moved on since you loaded it.' }, { status: 409 })
        : Response.json({});
  }
  vi.stubGlobal('fetch', vi.fn(apiStub(routes)));
}

function renderMoves() {
  render(WithQueryClient, { props: { component: MovesHarness, props: { applications: [casey, jordan, sam], to: screening } } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('moving several applications', () => {
  it('announces every failure, not just the last move, and keeps only the failed ones selected', async () => {
    stubApi(new Set([jordan.id]));
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move all' }));

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent(
        'Moved 2 of 3 to Screening. Could not move Jordan Lee: This application moved on since you loaded it.',
      ),
    );
    expect(within(screen.getByRole('list', { name: 'Still selected' })).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      jordan.id,
    ]);
  });

  it('says how many moved when all of them did, and leaves nothing selected', async () => {
    stubApi(new Set());
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move all' }));

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Moved 3 candidates to Screening.'));
    expect(within(screen.getByRole('list', { name: 'Still selected' })).queryAllByRole('listitem')).toHaveLength(0);
  });
});
