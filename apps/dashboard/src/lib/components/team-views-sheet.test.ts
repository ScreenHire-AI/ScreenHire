import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, applications, savedView } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import type { ViewActions } from '../views.svelte.js';
import Sheet from './team-views-sheet.svelte';

const seenAt = '2026-10-10T00:00:00Z';

const shared = (patch: Parameters<typeof savedView>[0] = {}) =>
  savedView({ shared: true, mine: false, canEdit: false, inSidebar: false, ownerName: 'Jordan Ellis', sharedAt: '2026-10-05T00:00:00Z', ...patch });

const actions = () => ({
  rename: vi.fn<ViewActions['rename']>(async () => null),
  setShared: vi.fn<ViewActions['setShared']>(async () => null),
  remove: vi.fn<ViewActions['remove']>(async () => null),
  setHidden: vi.fn<ViewActions['setHidden']>(async () => null),
  addToSidebar: vi.fn<ViewActions['addToSidebar']>(async () => null),
  removeFromSidebar: vi.fn<ViewActions['removeFromSidebar']>(async () => null),
  markTeamViewsOpened: vi.fn<ViewActions['markTeamViewsOpened']>(async () => null),
});

function show(views: ReturnType<typeof shared>[], viewActions = actions()) {
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/applications': () => Response.json(applications) })));
  render(WithQueryClient, { props: { component: Sheet, props: { open: true, views, seenAt, jobTitles: {}, actions: viewActions } } });
  return viewActions;
}

afterEach(() => vi.unstubAllGlobals());

describe('the sheet of team views', () => {
  it("lists a teammate's shared view with who shared it, its filters in one line and how many candidates it shows now", async () => {
    show([shared({ name: 'Applied this week', filters: { ...savedView().filters, stages: ['Applied'] } })]);

    const sheet = await screen.findByRole('dialog', { name: 'Team views' });
    expect(within(sheet).getByRole('heading', { name: 'Applied this week' })).toBeInTheDocument();
    expect(within(sheet).getByText('Shared by Jordan Ellis')).toBeInTheDocument();
    expect(within(sheet).getByText('Stage Applied')).toBeInTheDocument();
    // The fixtures have two candidates in Applied.
    expect(await within(sheet).findByText('Shows 2 candidates now')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("lists only the views a teammate shared: not the person's own, and not a private one", async () => {
    show([shared({ id: 'a', name: 'Team queue' }), shared({ id: 'b', name: 'Mine, shared', mine: true, inSidebar: true }), savedView({ id: 'c', name: 'Private' })]);

    const sheet = await screen.findByRole('dialog', { name: 'Team views' });
    expect(within(sheet).getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual(['Team queue']);
  });

  it('marks a view New when it was shared after the person last opened the sheet and is not in their sidebar, and only then', async () => {
    show([
      shared({ id: 'a', name: 'Shared since', sharedAt: '2026-10-12T00:00:00Z' }),
      shared({ id: 'b', name: 'Shared before', sharedAt: '2026-10-05T00:00:00Z' }),
      shared({ id: 'c', name: 'Already added', sharedAt: '2026-10-11T00:00:00Z', inSidebar: true }),
    ]);

    const sheet = await screen.findByRole('dialog', { name: 'Team views' });
    const since = within(sheet).getByRole('heading', { name: 'Shared since' }).closest('li')!;
    const before = within(sheet).getByRole('heading', { name: 'Shared before' }).closest('li')!;
    const added = within(sheet).getByRole('heading', { name: 'Already added' }).closest('li')!;
    expect(within(since).getByText('New')).toBeInTheDocument();
    expect(within(before).queryByText('New')).toBeNull();
    expect(within(added).queryByText('New')).toBeNull();
    // The newest share comes first.
    expect(within(sheet).getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual([
      'Shared since',
      'Already added',
      'Shared before',
    ]);
  });

  it('notes the visit once it opens', async () => {
    const viewActions = show([shared()]);

    await screen.findByRole('dialog', { name: 'Team views' });
    await waitFor(() => expect(viewActions.markTeamViewsOpened).toHaveBeenCalledOnce());
  });

  it('previews a view without adding it, and adds or removes it from the sidebar', async () => {
    const viewActions = show([shared({ id: 'a', name: 'Team queue' }), shared({ id: 'b', name: 'Already here', inSidebar: true })]);

    const sheet = await screen.findByRole('dialog', { name: 'Team views' });
    const queue = within(sheet).getByRole('heading', { name: 'Team queue' }).closest('li')!;
    expect(within(queue).getByRole('link', { name: 'Preview Team queue' })).toHaveAttribute('href', '/candidates?view=a');
    expect(viewActions.addToSidebar).not.toHaveBeenCalled();

    await fireEvent.click(within(queue).getByRole('button', { name: 'Add Team queue to your sidebar' }));
    expect(viewActions.addToSidebar).toHaveBeenCalledWith(expect.objectContaining({ id: 'a' }));

    const here = within(sheet).getByRole('heading', { name: 'Already here' }).closest('li')!;
    expect(within(here).getByText('In your sidebar')).toBeInTheDocument();
    expect(within(here).queryByRole('button', { name: /^Add/ })).toBeNull();
    await fireEvent.click(within(here).getByRole('button', { name: 'Remove Already here from your sidebar' }));
    expect(viewActions.removeFromSidebar).toHaveBeenCalledWith(expect.objectContaining({ id: 'b' }));
  });

  it('says why an add failed, in the sheet', async () => {
    const viewActions = actions();
    viewActions.addToSidebar.mockResolvedValueOnce('Could not add "Team queue" to your sidebar (HTTP 404).');
    show([shared({ name: 'Team queue' })], viewActions);

    await fireEvent.click(await screen.findByRole('button', { name: 'Add Team queue to your sidebar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not add "Team queue" to your sidebar (HTTP 404).');
  });

  it("says a failure on the row that failed, not at the sheet's top", async () => {
    const viewActions = actions();
    viewActions.addToSidebar.mockResolvedValueOnce('Could not add "Team queue" to your sidebar (HTTP 404).');
    show([shared({ id: 'a', name: 'Team queue' }), shared({ id: 'b', name: 'Other view' })], viewActions);

    await fireEvent.click(await screen.findByRole('button', { name: 'Add Team queue to your sidebar' }));

    const alert = await screen.findByRole('alert');
    expect(alert.closest('li')).toHaveTextContent('Team queue');
    expect(alert.closest('li')).not.toHaveTextContent('Other view');
  });

  it('holds the row\'s button while its request runs, so a second press sends nothing', async () => {
    const viewActions = actions();
    let finish: (value: null) => void = () => {};
    viewActions.addToSidebar.mockReturnValueOnce(new Promise((resolve) => { finish = resolve; }));
    show([shared({ name: 'Team queue' })], viewActions);

    const add = await screen.findByRole('button', { name: 'Add Team queue to your sidebar' });
    await fireEvent.click(add);
    // A disabled button takes no press in a browser, which is what stops the second request.
    expect(add).toBeDisabled();
    expect(viewActions.addToSidebar).toHaveBeenCalledTimes(1);

    finish(null);
    await waitFor(() => expect(add).toBeEnabled());
  });

  it('holds each row on its own while two requests run, and keeps the failure of the one that failed', async () => {
    const viewActions = actions();
    let finishFirst: (value: string | null) => void = () => {};
    let finishSecond: (value: string | null) => void = () => {};
    viewActions.addToSidebar
      .mockReturnValueOnce(new Promise((resolve) => { finishFirst = resolve; }))
      .mockReturnValueOnce(new Promise((resolve) => { finishSecond = resolve; }));
    show([shared({ id: 'a', name: 'Team queue' }), shared({ id: 'b', name: 'Other view' })], viewActions);

    const first = await screen.findByRole('button', { name: 'Add Team queue to your sidebar' });
    const second = screen.getByRole('button', { name: 'Add Other view to your sidebar' });
    await fireEvent.click(first);
    await fireEvent.click(second);
    expect(first).toBeDisabled();
    expect(second).toBeDisabled();

    // The first finishing with a failure frees only its own row, which says so; the second still waits.
    finishFirst('Could not add "Team queue" to your sidebar (HTTP 404).');
    await waitFor(() => expect(first).toBeEnabled());
    expect(second).toBeDisabled();
    expect((await screen.findByRole('alert')).closest('li')).toHaveTextContent('Team queue');

    finishSecond(null);
    await waitFor(() => expect(second).toBeEnabled());
    expect(screen.getByRole('alert').closest('li')).toHaveTextContent('Team queue');
  });

  it('stops saying it is counting when the candidates cannot be read', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/applications': () => new Response('{}', { status: 500 }) })));
    render(WithQueryClient, { props: { component: Sheet, props: { open: true, views: [shared()], seenAt, jobTitles: {}, actions: actions() } } });

    await screen.findByRole('heading', { name: shared().name });
    await waitFor(() => expect(screen.queryByText('Counting candidates…')).not.toBeInTheDocument());
  });

  it('says so when nobody has shared a view, and where to share one', async () => {
    show([savedView()]);

    expect(await screen.findByText(/Nobody has shared a view yet/)).toBeInTheDocument();
    expect(screen.getByText(/open its menu in the sidebar and choose Share with the team/)).toBeInTheDocument();
  });
});
