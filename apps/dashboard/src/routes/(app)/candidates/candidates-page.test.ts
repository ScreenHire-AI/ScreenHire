import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ApplicationSummary, SavedViewSummary } from '@screenhire/api-client';
import { goto } from '$app/navigation';
import { apiStub, applications, savedView, user, userAs, viewList } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Signed out at /candidates unless a test changes it.
const appPage = vi.hoisted(() => ({ url: new URL('http://dashboard.test/candidates'), data: {} as Record<string, unknown> }));
// The Add candidate sheet's form reads `navigating` (superforms).
vi.mock('$app/state', () => ({ page: appPage, navigating: { to: null } }));
vi.mock('$app/navigation', async (importOriginal) => ({ ...(await importOriginal<typeof import('$app/navigation')>()), goto: vi.fn(async () => {}) }));

afterEach(() => {
  appPage.url = new URL('http://dashboard.test/candidates');
  appPage.data = {};
  vi.unstubAllGlobals();
});

const render_ = (rows: ApplicationSummary[] = applications, reachedIds: string[] = []) => {
  const stub = apiStub({
    'GET /v1/applications': () => Response.json(rows),
    'GET /v1/email-templates': () => Response.json({ sendingEnabled: true, templates: [], variables: [] }),
    'GET /v1/views': () => Response.json(viewList()),
  });
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
      // A report's funnel link asks which applications reached the stage; the address carries the dates and jobs too.
      String(input).startsWith('/v1/reports/hiring/funnel/Interview/applications') ? Response.json(reachedIds) : stub(input, init),
    ),
  );
  return render(WithQueryClient, { props: { component: Page, props: {} } });
};

describe('candidates page', () => {
  it('shows the title with a count pill in the header band (section 7, T4)', async () => {
    render_();

    const heading = await screen.findByRole('heading', { level: 1, name: 'Candidates' });
    await waitFor(() => expect(screen.getByText(`${applications.length}`, { selector: '[data-slot="badge"]' })).toBeInTheDocument());
    const count = screen.getByText(`${applications.length}`, { selector: '[data-slot="badge"]' });
    expect(count).toHaveClass('min-w-7');
    expect(count).toHaveTextContent(`${applications.length} applications`);
    expect(heading.closest('[data-slot="header-band"]')).toContainElement(count);
  });

  it('holds the search in the table panel header and the filters in a row under it, and the search narrows the rows and the count', async () => {
    render_();
    const panel = await screen.findByRole('region', { name: 'Applications' });
    const header = panel.querySelector('header')!;
    await screen.findByRole('link', { name: 'Casey Morgan' });

    expect(within(header).getByRole('searchbox', { name: 'Search candidates' })).toBeInTheDocument();
    const filters = panel.querySelector<HTMLElement>('[data-filters]')!;
    expect(within(filters).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Job: All', 'Stage: All', 'Verification: all', 'Tag: all']);

    await fireEvent.input(within(header).getByRole('searchbox', { name: 'Search candidates' }), { target: { value: 'jordan' } });

    expect(screen.getAllByRole('link', { name: /^(Casey Morgan|Jordan Lee|Sam Taylor)$/ }).map((link) => link.textContent)).toEqual(['Jordan Lee']);
    expect(screen.getByText('1', { selector: '[data-slot="badge"]' })).toHaveTextContent('1 application');
  });

  it('filters by a tag the listed applications carry, from the filter row', async () => {
    render_();
    const panel = await screen.findByRole('region', { name: 'Applications' });
    await screen.findByRole('link', { name: 'Casey Morgan' });

    const trigger = within(panel.querySelector<HTMLElement>('[data-filters]')!).getByRole('button', { name: /^Tag: all/ });
    await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).getAllByRole('menuitemradio').map((item) => item.textContent?.trim())).toEqual(['Any tag', 'Referral']);
    await fireEvent.click(within(menu).getByRole('menuitemradio', { name: 'Referral' }));

    await waitFor(() =>
      expect(screen.getAllByRole('link', { name: /^(Casey Morgan|Jordan Lee|Sam Taylor)$/ }).map((link) => link.textContent)).toEqual(['Jordan Lee']),
    );
    expect(within(panel.querySelector<HTMLElement>('[data-filters]')!).getByRole('button', { name: /^Tag: Referral/ })).toBeInTheDocument();
  });

  it('filters by several jobs and several stages at once, each choice a pill with its own remove button', async () => {
    const other = '01a157d4-0000-7000-8000-0000000009aa';
    const rows: ApplicationSummary[] = [
      ...applications,
      { ...applications[0]!, id: '01a157d4-0000-7000-8000-0000000001aa', candidateName: 'Riley Shaw', jobId: other, jobTitle: 'Support Engineer' },
      {
        ...applications[0]!,
        id: '01a157d4-0000-7000-8000-0000000001ab',
        candidateName: 'Avery Stone',
        jobId: '01a157d4-0000-7000-8000-0000000009ab',
        jobTitle: 'Product Designer',
      },
    ];
    render_(rows);
    const panel = await screen.findByRole('region', { name: 'Applications' });
    const filters = panel.querySelector<HTMLElement>('[data-filters]')!;
    await screen.findByRole('link', { name: 'Casey Morgan' });
    const names = () => screen.getAllByRole('link', { name: /^(Casey Morgan|Jordan Lee|Sam Taylor|Riley Shaw|Avery Stone)$/ }).map((link) => link.textContent).toSorted();

    await fireEvent.pointerDown(within(filters).getByRole('button', { name: 'Job: All' }), { button: 0, pointerType: 'mouse' });
    const jobs = await screen.findByRole('menu');
    expect(within(jobs).getAllByRole('menuitemcheckbox').map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Product Designer 1',
      'Senior .NET Engineer 3',
      'Support Engineer 1',
    ]);
    await fireEvent.click(within(jobs).getByRole('menuitemcheckbox', { name: /Senior \.NET Engineer/ }));
    await fireEvent.click(within(jobs).getByRole('menuitemcheckbox', { name: /Support Engineer/ }));
    await waitFor(() => expect(names()).toEqual(['Casey Morgan', 'Jordan Lee', 'Riley Shaw', 'Sam Taylor']));
    expect(within(filters).getByRole('button', { name: 'Job: 2 chosen' })).toBeInTheDocument();
    await fireEvent.keyDown(jobs, { key: 'Escape' });

    // Stages are matched by name across the picked jobs.
    await fireEvent.pointerDown(within(filters).getByRole('button', { name: 'Stage: All' }), { button: 0, pointerType: 'mouse' });
    const stageMenu = await screen.findByRole('menu');
    await fireEvent.click(within(stageMenu).getByRole('menuitemcheckbox', { name: new RegExp(applications[0]!.stage.name) }));
    await waitFor(() => expect(names()).toEqual(['Casey Morgan', 'Riley Shaw', 'Sam Taylor']));
    await fireEvent.keyDown(stageMenu, { key: 'Escape' });

    await fireEvent.click(within(filters).getByRole('button', { name: 'Remove Job Support Engineer' }));
    await waitFor(() => expect(names()).toEqual(['Casey Morgan', 'Sam Taylor']));
  });

  it('counts the ticked rows in a pill in the panel header, as the table has no footer (K1, W3)', async () => {
    render_();
    await screen.findByRole('link', { name: 'Casey Morgan' });
    expect(document.querySelector('[data-selected-count]')).toBeNull();

    await fireEvent.click(screen.getByRole('checkbox', { name: `Select ${applications[0]!.candidateName}` }));

    // The count sits in the count badge after its label (T4).
    const selected = document.querySelector<HTMLElement>('[data-selected-count]')!;
    expect(selected).toHaveTextContent('Selected 1');
    expect(selected.querySelector('[data-slot="badge"]')).toHaveClass('min-w-7');
    expect(screen.getByRole('region', { name: 'Applications' }).querySelector('header')).toContainElement(selected);
  });

  it('makes Add candidate the primary action for a role that adds candidates, and opens its sheet with a job picker', async () => {
    appPage.data = { user };
    render_();

    const band = document.querySelector<HTMLElement>('[data-slot=header-band]')!;
    await fireEvent.click(await within(band).findByRole('button', { name: 'Add candidate' }));
    const sheet = await screen.findByRole('dialog', { name: 'Add candidate' });
    expect(within(sheet).getByLabelText('Job')).toBeInTheDocument();
  });

  it('has no Add candidate for a role that cannot add candidates (A3)', async () => {
    appPage.data = { user: userAs('Viewer') };
    render_();

    await screen.findByRole('heading', { level: 1, name: 'Candidates' });
    expect(screen.queryByRole('button', { name: 'Add candidate' })).not.toBeInTheDocument();
  });

  it('offers Email for the ticked rows, and none in a view that verification picked (ADR-0004)', async () => {
    appPage.data = { user };
    const flagged = applications.map((application) => ({
      ...application,
      verification: {
        authenticity: 'ReviewSuggested' as const,
        authenticityReasons: ['RESUME_TEMPLATE_MATCH'],
        authenticityDismissed: false,
        aiAssistance: 'NotIndicated' as const,
        aiAssistanceReasons: [],
        aiAssistanceDismissed: false,
      },
    }));
    const { unmount } = render_(flagged);
    await fireEvent.click(await screen.findByRole('checkbox', { name: `Select ${applications[0]!.candidateName}` }));
    const header = screen.getByRole('region', { name: 'Applications' }).querySelector('header')!;
    expect(await within(header).findByRole('button', { name: 'Email 1 selected' })).toBeInTheDocument();
    unmount();

    appPage.url = new URL('http://dashboard.test/candidates?view=needs-review');
    render_(flagged);
    await fireEvent.click(await screen.findByRole('checkbox', { name: `Select ${applications[0]!.candidateName}` }));
    await waitFor(() => expect(document.querySelector('[data-selected-count]')).toHaveTextContent('Selected 1'));
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.queryByRole('button', { name: /^Email/ })).toBeNull();
  });

  it('shows only the applications a report row counted, from its link, and clears that with the chip', async () => {
    const [casey, jordan] = applications;
    const from = '2026-10-01T00:00:00.000Z';
    const to = '2026-10-31T00:00:00.000Z';
    appPage.url = new URL(`http://dashboard.test/candidates?reached=Interview&from=${from}&to=${to}`);
    render_(applications, [jordan!.id]);

    expect(await screen.findByRole('link', { name: jordan!.candidateName })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: casey!.candidateName })).toBeNull();
    expect(screen.getByText('1', { selector: '[data-slot="badge"]' })).toHaveTextContent('1 application');

    await fireEvent.click(screen.getByRole('button', { name: /^Clear filter: Reached Interview, / }));
    const [url] = vi.mocked(goto).mock.calls.at(-1)!;
    expect((url as URL).search).toBe('');
  });

  it('starts with the jobs the link names', async () => {
    const [casey, jordan] = applications;
    appPage.url = new URL(`http://dashboard.test/candidates?job=${casey!.jobId}`);
    render_();

    await screen.findByRole('link', { name: casey!.candidateName });
    const others = applications.filter((application) => application.jobId !== casey!.jobId);
    for (const other of others) expect(screen.queryByRole('link', { name: other.candidateName })).toBeNull();
    expect(jordan).toBeDefined();
  });
});

// Views people save (docs/architecture/saved-views.md): the Save view button, the dialog, and opening a saved view.
describe('candidates page views', () => {
  const view = savedView({ filters: { ...savedView().filters, tag: 'Referral', sortColumn: 'candidate', sortDescending: false } });
  const names = () => screen.getAllByRole('link', { name: /^(Casey Morgan|Jordan Lee|Sam Taylor)$/ }).map((link) => link.textContent);
  const stubViews = (saved: SavedViewSummary[], extra: Parameters<typeof apiStub>[0] = {}) => {
    const fetchStub = vi.fn(
      apiStub({
        'GET /v1/applications': () => Response.json(applications),
        'GET /v1/email-templates': () => Response.json({ sendingEnabled: true, templates: [], variables: [] }),
        'GET /v1/views': () => Response.json(viewList(saved)),
        ...extra,
      }),
    );
    vi.stubGlobal('fetch', fetchStub);
    return fetchStub;
  };
  const filterRow = (panel: HTMLElement) => within(panel.querySelector<HTMLElement>('[data-filters]')!);
  const pickTag = async (panel: HTMLElement) => {
    await fireEvent.pointerDown(filterRow(panel).getByRole('button', { name: /^Tag: / }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitemradio', { name: 'Referral' }));
  };

  it('offers Save view at the end of the filter row only once the filters differ from the view it is on', async () => {
    stubViews([]);
    render(WithQueryClient, { props: { component: Page, props: {} } });
    const panel = await screen.findByRole('region', { name: 'Applications' });
    await screen.findByRole('link', { name: 'Casey Morgan' });
    expect(filterRow(panel).queryByRole('button', { name: 'Save view' })).toBeNull();

    await pickTag(panel);

    const buttons = filterRow(panel).getAllByRole('button');
    expect(buttons.at(-1)).toHaveAccessibleName('Save view');
    expect(buttons.at(-1)).toHaveAttribute('aria-haspopup', 'dialog');
  });

  describe('what a saved view keeps of the verification guardrails (ADR-0004)', () => {
    const flagged = applications.map((application) => ({
      ...application,
      verification: {
        authenticity: 'ReviewSuggested' as const,
        authenticityReasons: ['RESUME_TEMPLATE_MATCH'],
        authenticityDismissed: false,
        aiAssistance: 'NotIndicated' as const,
        aiAssistanceReasons: [],
        aiAssistanceDismissed: false,
      },
    }));
    const open = async (filters: Partial<SavedViewSummary['filters']>) => {
      const opened = savedView({ filters: { ...savedView().filters, ...filters } });
      appPage.data = { user };
      appPage.url = new URL(`http://dashboard.test/candidates?view=${opened.id}`);
      stubViews([opened], { 'GET /v1/applications': () => Response.json(flagged) });
      render(WithQueryClient, { props: { component: Page, props: {} } });
      await screen.findByRole('heading', { level: 1, name: opened.name });
      await fireEvent.click(await screen.findByRole('checkbox', { name: `Select ${applications[0]!.candidateName}` }));
      await waitFor(() => expect(document.querySelector('[data-selected-count]')).toHaveTextContent('Selected 1'));
    };

    it('offers Email in a view verification did not pick', async () => {
      await open({ sortColumn: 'candidate', sortDescending: false });

      expect(await screen.findByRole('button', { name: 'Email 1 selected' })).toBeInTheDocument();
    });

    it.each([
      ['an authenticity level', { authenticity: 'ReviewSuggested' }],
      ['an AI assistance level', { aiAssistance: 'NotIndicated' }],
      ['a built-in verification view', { baseView: 'needs-review' }],
    ])('offers no Email in a view that holds %s', async (_, filters) => {
      await open(filters);
      await new Promise((resolve) => setTimeout(resolve, 50));

      expect(screen.queryByRole('button', { name: /^Email/ })).toBeNull();
    });
  });

  it('saves the filters and sort under a name, not shared unless the switch is on, and opens the new view', async () => {
    const created = savedView({ id: '01a157d4-0000-7000-8000-0000000003bb', name: 'Warm referrals', shared: true });
    const fetchStub = stubViews([], { 'POST /v1/views': () => Response.json(created, { status: 201 }) });
    render(WithQueryClient, { props: { component: Page, props: {} } });
    const panel = await screen.findByRole('region', { name: 'Applications' });
    await screen.findByRole('link', { name: 'Casey Morgan' });
    await pickTag(panel);

    await fireEvent.click(filterRow(panel).getByRole('button', { name: 'Save view' }));
    const dialog = await screen.findByRole('dialog', { name: 'Save view' });
    expect(within(dialog).getByRole('switch', { name: 'Share with the team' })).not.toBeChecked();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Save view' }));
    expect(await within(dialog).findByText('Name the view.')).toBeInTheDocument();

    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: ' Warm referrals ' } });
    await fireEvent.click(within(dialog).getByRole('switch', { name: 'Share with the team' }));
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Save view' }));

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Save view' })).toBeNull());
    const post = fetchStub.mock.calls.find(([url, init]) => String(url) === '/v1/views' && init?.method === 'POST')!;
    expect(JSON.parse(String(post[1]!.body))).toEqual({
      name: 'Warm referrals',
      scope: 'Candidates',
      jobId: null,
      shared: true,
      filters: {
        baseView: null,
        jobIds: [],
        stages: [],
        authenticity: null,
        aiAssistance: null,
        tag: 'Referral',
        sortColumn: 'applied',
        sortDescending: true,
      },
    });
    expect(goto).toHaveBeenCalledWith(`/candidates?view=${created.id}`);
  });

  it('says why a name is refused, in the dialog', async () => {
    stubViews([], {
      'POST /v1/views': () => Response.json({ errors: { name: ['You already have a view with this name here.'] } }, { status: 400 }),
    });
    render(WithQueryClient, { props: { component: Page, props: {} } });
    const panel = await screen.findByRole('region', { name: 'Applications' });
    await screen.findByRole('link', { name: 'Casey Morgan' });
    await pickTag(panel);

    await fireEvent.click(filterRow(panel).getByRole('button', { name: 'Save view' }));
    const dialog = await screen.findByRole('dialog', { name: 'Save view' });
    await fireEvent.input(within(dialog).getByRole('textbox', { name: 'Name' }), { target: { value: 'Weekly' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Save view' }));

    expect(await within(dialog).findByText('You already have a view with this name here.')).toBeInTheDocument();
  });

  it('opens a saved view with its filters and sort on the page, and offers Save view only when they change', async () => {
    appPage.url = new URL(`http://dashboard.test/candidates?view=${view.id}`);
    stubViews([view]);
    render(WithQueryClient, { props: { component: Page, props: {} } });

    expect(await screen.findByRole('heading', { level: 1, name: 'Referrals in screening' })).toBeInTheDocument();
    await waitFor(() => expect(names()).toEqual(['Jordan Lee']));
    const panel = screen.getByRole('region', { name: 'Applications' });
    expect(filterRow(panel).getByRole('button', { name: /^Tag: Referral/ })).toBeInTheDocument();
    expect(within(panel).getByRole('columnheader', { name: /Candidate/ })).toHaveAttribute('aria-sort', 'ascending');
    expect(filterRow(panel).queryByRole('button', { name: 'Save view' })).toBeNull();
    expect(document.title).toBe('Referrals in screening · ScreenHire');

    await fireEvent.pointerDown(filterRow(panel).getByRole('button', { name: /^Tag: Referral/ }), { button: 0, pointerType: 'mouse' });
    await fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitemradio', { name: 'Any tag' }));

    expect(await filterRow(panel).findByRole('button', { name: 'Save view' })).toBeInTheDocument();
  });

  it('shows every candidate and says so when the view in the address is gone', async () => {
    appPage.url = new URL('http://dashboard.test/candidates?view=01a157d4-0000-7000-8000-0000000003ff');
    stubViews([]);
    render(WithQueryClient, { props: { component: Page, props: {} } });

    expect(await screen.findByText('That view is no longer there, so this shows every candidate.')).toBeInTheDocument();
    await waitFor(() => expect(names()).toHaveLength(applications.length));
    expect(screen.getByRole('heading', { level: 1, name: 'Candidates' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save view' })).toBeNull();
  });
});
