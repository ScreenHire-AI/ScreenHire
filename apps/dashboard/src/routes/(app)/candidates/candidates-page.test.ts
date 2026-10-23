import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ApplicationSummary } from '@screenhire/api-client';
import { apiStub, applications, user } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Signed out at /candidates unless a test changes it.
const appPage = vi.hoisted(() => ({ url: new URL('http://dashboard.test/candidates'), data: {} as Record<string, unknown> }));
vi.mock('$app/state', () => ({ page: appPage }));

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

  it('holds the search and the integrity filter in the table panel header, and the search narrows the rows and the count', async () => {
    render_();
    const panel = await screen.findByRole('region', { name: 'Applications' });
    const header = panel.querySelector('header')!;
    await screen.findByRole('link', { name: 'Casey Morgan' });

    expect(within(header).getByRole('searchbox', { name: 'Search candidates' })).toBeInTheDocument();
    expect(within(header).getByRole('button', { name: /^Integrity: all/ })).toBeInTheDocument();

    await fireEvent.input(within(header).getByRole('searchbox', { name: 'Search candidates' }), { target: { value: 'jordan' } });

    expect(screen.getAllByRole('link', { name: /^(Casey Morgan|Jordan Lee|Sam Taylor)$/ }).map((link) => link.textContent)).toEqual(['Jordan Lee']);
    expect(screen.getByText('1', { selector: '[data-slot="badge"]' })).toHaveTextContent('1 application');
  });

  it('counts the ticked rows in a pill in the panel header, as the table has no footer (K1, W3)', async () => {
    render_();
    await screen.findByRole('link', { name: 'Casey Morgan' });
    expect(screen.queryByText(/selected$/)).toBeNull();

    await fireEvent.click(screen.getByRole('checkbox', { name: `Select ${applications[0]!.candidateName}` }));

    const pill = screen.getByText('1 selected');
    expect(pill).toHaveAttribute('data-slot', 'badge');
    expect(screen.getByRole('region', { name: 'Applications' }).querySelector('header')).toContainElement(pill);
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
    expect(await screen.findByText('1 selected')).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.queryByRole('button', { name: /^Email/ })).toBeNull();
  });
});
