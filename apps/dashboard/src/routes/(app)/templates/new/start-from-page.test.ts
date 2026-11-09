// The new hiring template screen: three starting points ScreenHire ships as cards, a name that follows the chosen card until
// the person types their own, "Use this starting point" and "Start from nothing".
import type { CurrentUser, HiringTemplateSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import { startingPoints } from '#lib/starting-points.js';
import Page from './+page.svelte';

const { pageState, goto } = vi.hoisted(() => ({
  pageState: { data: { user: undefined as CurrentUser | undefined }, url: new URL('http://dashboard.test/templates/new') },
  goto: vi.fn(async (..._args: unknown[]) => {}),
}));
vi.mock('$app/state', () => ({ page: pageState, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto }));

const [office, trade, volume] = startingPoints;

const card = (point: (typeof startingPoints)[number], name: string, description: string): HiringTemplateSummary => ({
  id: point.templateId,
  name,
  description,
  isSystem: true,
  latestPublishedVersion: 1,
  latestPublishedVersionId: point.versionId,
  draftVersion: null,
  createdAt: '2026-10-01T09:00:00Z',
  updatedAt: null,
  lastUpdated: { at: null, by: 'ScreenHire' },
  archived: false,
});

const templates = [
  card(office!, 'Office role', 'Accountant, designer, analyst'),
  card(trade!, 'Trade role with a trial shift', 'Plumber, electrician, driver'),
  card(volume!, 'High volume', 'Shop assistant, warehouse picker'),
  // Not a starting point: the screen lists only the three it ships.
  { ...card(office!, 'Brightwater engineering', 'Ours'), id: 't-own', isSystem: false },
];

const stagesOf = (names: string[]) => ({
  definition: {
    workflow: {
      stages: [...names.map((name, index) => ({ id: `s${index}`, name, kind: index === 0 ? 'Applied' : 'Screening' })), { id: 'hired', name: 'Hired', kind: 'Hired' }, { id: 'rejected', name: 'Not progressing', kind: 'Rejected' }],
    },
  },
});

function renderPage(who: CurrentUser = user, routes: Parameters<typeof apiStub>[0] = {}) {
  pageState.data.user = who;
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/templates': () => Response.json(templates),
      [`GET /v1/templates/${office!.templateId}/versions/1`]: () => Response.json(stagesOf(['Applied', 'Phone screen', 'Interview'])),
      [`GET /v1/templates/${trade!.templateId}/versions/1`]: () => Response.json(stagesOf(['Applied', 'Trial shift'])),
      [`GET /v1/templates/${volume!.templateId}/versions/1`]: () => Response.json(stagesOf(['Applied', 'Quick call'])),
      'POST /v1/templates': () => Response.json({ id: 't-new' }, { status: 201 }),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  const view = render(WithQueryClient, { props: { component: Page, props: {} } });
  return { fetchMock, view };
}

const posted = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls.filter(([url, init]) => url === '/v1/templates' && init?.method === 'POST').map(([, init]) => JSON.parse(String(init.body)));
const nameField = () => screen.getByRole('textbox', { name: 'Template name' });
const radios = () => screen.getAllByRole('radio');

beforeEach(() => {
  pageState.data.user = user;
});

afterEach(() => {
  vi.unstubAllGlobals();
  goto.mockClear();
});

describe('start from page', () => {
  it('shows the three starting points as cards: name, an example line and the stages as plain text', async () => {
    const { view } = renderPage();

    await waitFor(() => expect(nameField()).toHaveValue('Office role'));
    const group = screen.getByRole('group', { name: 'Starting point' });
    expect(radios()).toHaveLength(3);
    const cards = radios().map((radio) => radio.closest('label')!);
    await waitFor(() => expect(cards[2]).toHaveTextContent('Applied, Quick call'));

    expect(cards[0]).toHaveTextContent('Office role');
    expect(cards[0]).toHaveTextContent('Accountant, designer, analyst');
    expect(cards[0]).toHaveTextContent('Applied, Phone screen, Interview');
    expect(cards[1]).toHaveTextContent('Trade role with a trial shift');
    expect(cards[1]).toHaveTextContent('Applied, Trial shift');
    expect(cards[2]).toHaveTextContent('High volume');
    // The end stages are the same everywhere, so a card leaves them out, and a template that is not a starting point is not a card.
    expect(group).not.toHaveTextContent('Hired');
    expect(group).not.toHaveTextContent('Brightwater engineering');
    await expectNoAxeViolations(view.container);
  });

  it('has the first card chosen, and one filled button, Use this starting point', async () => {
    renderPage();
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));

    expect(radios().map((radio) => (radio as HTMLInputElement).checked)).toEqual([true, false, false]);
    const filled = screen.getAllByRole('button').filter((button) => button.className.split(' ').includes('bg-primary'));
    expect(filled.map((button) => button.textContent?.trim())).toEqual(['Use this starting point']);
    expect(screen.getByRole('button', { name: 'Start from nothing' }).className).not.toContain('bg-primary');
  });

  it('puts the chosen card\'s name in the template name, and follows the choice while the name is untouched', async () => {
    renderPage();
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));

    await fireEvent.click(radios()[1]!);
    await waitFor(() => expect(nameField()).toHaveValue('Trade role with a trial shift'));
    await fireEvent.click(radios()[2]!);
    await waitFor(() => expect(nameField()).toHaveValue('High volume'));
    expect((radios()[2] as HTMLInputElement).checked).toBe(true);
  });

  it('keeps the name the person typed when another card is chosen', async () => {
    renderPage();
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));

    await fireEvent.input(nameField(), { target: { value: 'Front desk' } });
    await fireEvent.click(radios()[1]!);

    expect(nameField()).toHaveValue('Front desk');
  });

  it('creates a copy of the chosen starting point\'s version with the name, then opens it', async () => {
    const { fetchMock } = renderPage();
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));
    await fireEvent.click(radios()[1]!);
    await waitFor(() => expect(nameField()).toHaveValue('Trade role with a trial shift'));
    await fireEvent.input(nameField(), { target: { value: 'Site crew' } });

    await fireEvent.click(screen.getByRole('button', { name: 'Use this starting point' }));

    await waitFor(() => expect(goto).toHaveBeenCalledExactlyOnceWith('/templates/t-new'));
    expect(posted(fetchMock)).toEqual([{ name: 'Site crew', copyFromVersionId: trade!.versionId }]);
  });

  it('uses the card\'s own name when the name is left alone, and the first card\'s version', async () => {
    const { fetchMock } = renderPage();
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));

    await fireEvent.click(screen.getByRole('button', { name: 'Use this starting point' }));

    await waitFor(() => expect(goto).toHaveBeenCalledWith('/templates/t-new'));
    expect(posted(fetchMock)).toEqual([{ name: 'Office role', copyFromVersionId: office!.versionId }]);
  });

  it('starts from nothing: the basic pipeline, with no version to copy', async () => {
    const { fetchMock } = renderPage();
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));
    await fireEvent.input(nameField(), { target: { value: 'Seasonal staff' } });

    await fireEvent.click(screen.getByRole('button', { name: 'Start from nothing' }));

    await waitFor(() => expect(goto).toHaveBeenCalledExactlyOnceWith('/templates/t-new'));
    expect(posted(fetchMock)).toEqual([{ name: 'Seasonal staff', copyFromVersionId: null }]);
  });

  it('names a template started from nothing and left unnamed "New hiring template"', async () => {
    const { fetchMock } = renderPage();
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));
    await fireEvent.input(nameField(), { target: { value: '   ' } });

    await fireEvent.click(screen.getByRole('button', { name: 'Start from nothing' }));

    await waitFor(() => expect(posted(fetchMock)).toEqual([{ name: 'New hiring template', copyFromVersionId: null }]));
  });

  it('says so, and stays, when the template could not be created', async () => {
    renderPage(user, { 'POST /v1/templates': () => Response.json({ title: 'Conflict', status: 409, detail: 'A template with that name exists.' }, { status: 409 }) });
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));

    await fireEvent.click(screen.getByRole('button', { name: 'Use this starting point' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('A template with that name exists.'));
    expect(goto).not.toHaveBeenCalled();
  });

  it('is read only without the right to edit templates: a line to ask an owner, and no cards or buttons', async () => {
    renderPage(userAs('Viewer'));

    expect(await screen.findByText('Ask an owner or admin to make hiring templates.')).toBeInTheDocument();
    expect(screen.queryByRole('radio')).toBeNull();
    expect(screen.queryByRole('textbox')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Use this starting point' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Start from nothing' })).toBeNull();
  });

  it('has the name panel without a visible title, and Start from as the panel that holds the cards', async () => {
    renderPage();
    await waitFor(() => expect(nameField()).toHaveValue('Office role'));

    expect(within(screen.getByRole('region', { name: 'Start from' })).getAllByRole('radio')).toHaveLength(3);
    expect(screen.getByRole('region', { name: 'Name' })).toContainElement(nameField());
    // The panel is named for screen readers only: its heading is visually hidden.
    expect(screen.getByRole('heading', { name: 'Name' })).toHaveClass('sr-only');
  });
});
