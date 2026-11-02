import type { ApplicationSummary, CurrentUser } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, applications, jobId, user, userAs } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import CloseJobDialog from './close-job-dialog.svelte';

const appPage = vi.hoisted(() => ({ data: {} as Record<string, unknown> }));
vi.mock('$app/state', () => ({ page: appPage }));

afterEach(() => {
  appPage.data = {};
  vi.unstubAllGlobals();
});

const [casey, jordan, sam] = applications as [ApplicationSummary, ApplicationSummary, ApplicationSummary];
const closePath = `/v1/jobs/${jobId}/close`;

function renderDialog(rows: ApplicationSummary[], as: CurrentUser = user) {
  appPage.data = { user: as };
  const calls: string[] = [];
  const bodies: Record<string, unknown> = {};
  const record = (key: string, body: unknown) => {
    calls.push(key);
    bodies[key] = body;
  };
  const fetchMock = vi.fn(
    apiStub({
      [`GET /v1/jobs/${jobId}/applications`]: () => Response.json(rows),
      'POST /v1/talent-pool': (init) => {
        record('keep', JSON.parse(String(init?.body)));
        return Response.json({ kept: rows.length, notAllowed: 0, alreadyEnded: 0 });
      },
      [`POST ${closePath}`]: () => {
        record('close', null);
        return Response.json({});
      },
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  const view = render(WithQueryClient, { props: { component: CloseJobDialog, props: { jobId, open: true } } });
  return { ...view, calls, bodies, fetchMock };
}

const dialog = () => screen.findByRole('dialog', { name: 'Close this job and remove it everywhere?' });

describe('close job dialog', () => {
  it('offers to save the open candidates who allowed it, counting only them', async () => {
    renderDialog([
      { ...casey, allowsFutureRoles: true },
      { ...jordan, allowsFutureRoles: true },
      // Never gave permission, took it back, already saved, and rejected: none of them count.
      sam,
      { ...casey, id: 'a-taken-back', allowsFutureRoles: false },
      { ...casey, id: 'a-kept', allowsFutureRoles: true, keptInMindAt: '2026-10-25T09:00:00Z' },
      { ...casey, id: 'a-rejected', allowsFutureRoles: true, stage: { id: 'rejected', name: 'Rejected', kind: 'Rejected' } },
    ]);
    const box = await within(await dialog()).findByRole('checkbox', { name: 'Also save the 2 candidates who allowed it' });

    expect(box).not.toBeChecked();
    await expectNoAxeViolations(document.body);
  });

  it('says "the candidate" when exactly one allowed it', async () => {
    renderDialog([{ ...casey, allowsFutureRoles: true }, sam]);

    expect(await within(await dialog()).findByRole('checkbox', { name: 'Also save the candidate who allowed it' })).toBeInTheDocument();
  });

  it('saves them first and closes the job after, when the box is ticked', async () => {
    const { calls, bodies } = renderDialog([{ ...casey, allowsFutureRoles: true }, { ...jordan, allowsFutureRoles: true }, sam]);
    const box = await screen.findByRole('checkbox', { name: 'Also save the 2 candidates who allowed it' });

    await fireEvent.click(box);
    expect(box).toBeChecked();
    await fireEvent.click(screen.getByRole('button', { name: 'Close & remove everywhere' }));

    await waitFor(() => expect(calls).toEqual(['keep', 'close']));
    expect(bodies.keep).toEqual({ applicationIds: [casey.id, jordan.id] });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('only closes the job when the box is left unticked', async () => {
    const { calls } = renderDialog([{ ...casey, allowsFutureRoles: true }]);
    await screen.findByRole('checkbox', { name: /^Also save the/ });

    await fireEvent.click(screen.getByRole('button', { name: 'Close & remove everywhere' }));

    await waitFor(() => expect(calls).toEqual(['close']));
  });

  it('does not close the job when saving them fails, and says so', async () => {
    appPage.data = { user };
    const close = vi.fn(() => Response.json({}));
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          [`GET /v1/jobs/${jobId}/applications`]: () => Response.json([{ ...casey, allowsFutureRoles: true }]),
          'POST /v1/talent-pool': () => Response.json({ detail: 'Someone else moved them.' }, { status: 409 }),
          [`POST ${closePath}`]: close,
        }),
      ),
    );
    render(WithQueryClient, { props: { component: CloseJobDialog, props: { jobId, open: true } } });
    await fireEvent.click(await screen.findByRole('checkbox', { name: /^Also save the/ }));

    await fireEvent.click(screen.getByRole('button', { name: 'Close & remove everywhere' }));

    await waitFor(() => expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('Someone else moved them.'));
    expect(close).not.toHaveBeenCalled();
  });

  it('shows no checkbox when nobody allowed it, and closes the job as before', async () => {
    const { calls, fetchMock } = renderDialog([casey, { ...jordan, allowsFutureRoles: false }]);
    await dialog();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(`/v1/jobs/${jobId}/applications`, expect.anything()));

    expect(screen.queryByRole('checkbox')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Close & remove everywhere' }));
    await waitFor(() => expect(calls).toEqual(['close']));
  });

  it('shows no checkbox, and never reads the job\'s applications, without Move candidates', async () => {
    const viewer = userAs('Viewer');
    const { fetchMock } = renderDialog([{ ...casey, allowsFutureRoles: true }], viewer);
    await dialog();

    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(fetchMock).not.toHaveBeenCalledWith(`/v1/jobs/${jobId}/applications`, expect.anything());
  });
});
