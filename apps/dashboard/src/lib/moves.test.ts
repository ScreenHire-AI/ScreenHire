import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { preferences } from '#lib/preferences.svelte.js';
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
  preferences.setConfirmMoves(true);
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

// The question before a move into a stage that has automations (design system section 11, Move confirmation).
describe('confirming a move', () => {
  const email = { id: 'email-on-screening', trigger: { type: 'StageEntered', stageId: 'screening' }, actions: [{ type: 'SendEmail', emailTemplate: 'stage-change' }] };

  function stubJob(automations: unknown[], onMove?: () => void | Promise<void>) {
    const posted: string[] = [];
    const routes: Parameters<typeof apiStub>[0] = {
      [`GET /v1/jobs/${jobId}`]: () => Response.json({ id: jobId, workflow: { stages, questionnaire: { questions: [] }, automations } }),
      'GET /v1/organization/members': () => Response.json([]),
      'GET /v1/email-templates': () => Response.json({ sendingEnabled: true, templates: [], variables: [] }),
      [`GET /v1/jobs/${jobId}/applications`]: () => Response.json(applications),
      'GET /v1/applications': () => Response.json(applications),
      'GET /v1/candidates': () => Response.json([]),
    };
    for (const application of applications) {
      routes[`POST /v1/applications/${application.id}/move`] = async () => {
        posted.push(application.id);
        await onMove?.();
        return Response.json({});
      };
    }
    vi.stubGlobal('fetch', vi.fn(apiStub(routes)));
    return posted;
  }

  it('lists what entering the stage does and moves only on Move', async () => {
    const posted = stubJob([email]);
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));

    const dialog = await screen.findByRole('dialog', { name: `Move ${casey.candidateName} to Screening?` });
    expect(within(dialog).getByRole('list', { name: 'What happens' })).toHaveTextContent('Send the "Moved to the next step" email.');
    expect(posted).toEqual([]);

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Move' }));

    await waitFor(() => expect(screen.getByTestId('moved-one')).toHaveTextContent('moved'));
    expect(posted).toEqual([casey.id]);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('does nothing on Cancel, and says the move did not happen', async () => {
    const posted = stubJob([email]);
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));
    await fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.getByTestId('moved-one')).toHaveTextContent('not moved'));
    expect(posted).toEqual([]);
    expect(screen.getByRole('status').textContent).toBe('');
  });

  // A menu as bits-ui draws it: the open trigger names its menu in aria-controls, and the chosen item has the focus.
  function openMenu() {
    const opener = Object.assign(document.createElement('button'), { textContent: 'Move to stage' });
    opener.setAttribute('aria-controls', 'stage-menu');
    const menu = Object.assign(document.createElement('div'), { id: 'stage-menu' });
    menu.setAttribute('role', 'menu');
    const item = Object.assign(document.createElement('div'), { tabIndex: -1, textContent: 'Screening' });
    item.setAttribute('role', 'menuitem');
    menu.append(item);
    document.body.append(opener, menu);
    item.focus();
    return { opener, menu };
  }

  it('gives the focus back to the button whose menu started the move on Cancel, not to the menu item that is gone', async () => {
    stubJob([email]);
    renderMoves();
    const { opener, menu } = openMenu();

    try {
      await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));
      const dialog = await screen.findByRole('dialog');
      menu.remove();
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));

      await waitFor(() => expect(document.activeElement).toBe(opener));
    } finally {
      opener.remove();
    }
  });

  it('gives the focus back to that button after Move, once the move that disabled it is done', async () => {
    // The candidate page disables Move to stage while its move runs, which drops the focus to the page, and turns it back on
    // only once the mutation's state reaches the page, a little after the move answers.
    let opener: HTMLButtonElement | undefined;
    const posted = stubJob([email], async () => {
      await waitFor(() => expect(document.activeElement).toBe(opener));
      // A browser drops the focus from a button it disables; jsdom keeps it, so the test drops it first.
      opener!.blur();
      opener!.disabled = true;
      setTimeout(() => (opener!.disabled = false), 20);
    });
    renderMoves();
    const opened = openMenu();
    opener = opened.opener;

    try {
      await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));
      const dialog = await screen.findByRole('dialog');
      opened.menu.remove();
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Move' }));

      await waitFor(() => expect(posted).toEqual([casey.id]));
      await waitFor(() => expect(document.activeElement).toBe(opener));
    } finally {
      opener.remove();
    }
  });

  it('moves at once when the stage has no automations, with no dialog', async () => {
    const posted = stubJob([{ ...email, trigger: { type: 'StageEntered', stageId: 'offer' } }]);
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));

    await waitFor(() => expect(screen.getByTestId('moved-one')).toHaveTextContent('moved'));
    expect(posted).toEqual([casey.id]);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('moves at once, saying nothing of checks, when only a check-driven automation runs there (ADR-0004)', async () => {
    const posted = stubJob([{ ...email, conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'ReviewSuggested' }] } }]);
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));

    await waitFor(() => expect(posted).toEqual([casey.id]));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it("stops asking once Don't ask again is ticked, until the setting is turned back on", async () => {
    const posted = stubJob([email]);
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));
    const dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('checkbox', { name: "Don't ask again" }));
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Move' }));
    await waitFor(() => expect(posted).toHaveLength(1));
    expect(preferences.confirmMoves).toBe(false);

    await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));
    await waitFor(() => expect(posted).toHaveLength(2));
    expect(screen.queryByRole('dialog')).toBeNull();

    preferences.setConfirmMoves(true);
    await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
  });

  it('asks once for a bulk move, with the count in the title and the list said once', async () => {
    const posted = stubJob([email]);
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move all' }));

    const dialog = await screen.findByRole('dialog', { name: 'Move 3 candidates to Screening?' });
    expect(within(dialog).getByText('Each of them gets:')).toBeInTheDocument();
    expect(within(dialog.querySelector('ul')!).getAllByRole('listitem')).toHaveLength(1);
    expect(posted).toEqual([]);

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Move' }));

    await waitFor(() => expect(posted).toHaveLength(3));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('keeps every application selected when a bulk move is cancelled', async () => {
    stubJob([email]);
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move all' }));
    await fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    const still = await screen.findByRole('list', { name: 'Still selected' });
    expect(within(still).getAllByRole('listitem')).toHaveLength(3);
  });

  it('moves without the list when the job cannot be read', async () => {
    const posted: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          [`POST /v1/applications/${casey.id}/move`]: () => (posted.push(casey.id), Response.json({})),
          [`GET /v1/jobs/${jobId}/applications`]: () => Response.json(applications),
          'GET /v1/applications': () => Response.json(applications),
          'GET /v1/candidates': () => Response.json([]),
        }),
      ),
    );
    renderMoves();

    await fireEvent.click(screen.getByRole('button', { name: 'Move one' }));

    await waitFor(() => expect(posted).toEqual([casey.id]));
  });
});
