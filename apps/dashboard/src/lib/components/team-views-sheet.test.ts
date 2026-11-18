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
    expect(within(sheet).getByText('stage Applied')).toBeInTheDocument();
    // The fixtures have two candidates in Applied.
    expect(await within(sheet).findByText('Shows 2 candidates now')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("lists only the views a teammate shared: not the person's own, and not a private one", async () => {
    show([shared({ id: 'a', name: 'Team queue' }), shared({ id: 'b', name: 'Mine, shared', mine: true, inSidebar: true }), savedView({ id: 'c', name: 'Private' })]);

    const sheet = await screen.findByRole('dialog', { name: 'Team views' });
    expect(within(sheet).getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual(['Team queue']);
  });

  it('marks a view New when it was shared after the person last opened the sheet, and only then', async () => {
    show([
      shared({ id: 'a', name: 'Shared since', sharedAt: '2026-10-12T00:00:00Z' }),
      shared({ id: 'b', name: 'Shared before', sharedAt: '2026-10-05T00:00:00Z' }),
    ]);

    const sheet = await screen.findByRole('dialog', { name: 'Team views' });
    const since = within(sheet).getByRole('heading', { name: 'Shared since' }).closest('li')!;
    const before = within(sheet).getByRole('heading', { name: 'Shared before' }).closest('li')!;
    expect(within(since).getByText('New')).toBeInTheDocument();
    expect(within(before).queryByText('New')).toBeNull();
    // The newest share comes first.
    expect(within(sheet).getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual(['Shared since', 'Shared before']);
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

  it('says so when nobody has shared a view', async () => {
    show([savedView()]);

    expect(await screen.findByText(/Nobody has shared a view yet/)).toBeInTheDocument();
  });
});
