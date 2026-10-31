import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ApplicationSummary } from '@screenhire/api-client';
import { apiStub, applications, user, userAs } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Signed out at /candidates unless a test changes it.
const appPage = vi.hoisted(() => ({ url: new URL('http://dashboard.test/candidates'), data: {} as Record<string, unknown> }));
// The Add candidate sheet's form reads `navigating` (superforms).
vi.mock('$app/state', () => ({ page: appPage, navigating: { to: null } }));

afterEach(() => {
  appPage.url = new URL('http://dashboard.test/candidates');
  appPage.data = {};
  vi.unstubAllGlobals();
});

const render_ = (rows: ApplicationSummary[] = applications) => {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/applications': () => Response.json(rows),
        'GET /v1/email-templates': () => Response.json({ sendingEnabled: true, templates: [], variables: [] }),
      }),
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

  it('offers Email for the ticked rows, and none in a view that integrity picked (ADR-0004)', async () => {
    appPage.data = { user };
    const flagged = applications.map((application) => ({
      ...application,
      integrity: {
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
});
