import type { VerificationOverviewView } from '@screenhire/api-client';
import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../test-support/fixtures.js';
import { dollarLimits, limits, settingsWith } from '../../../test-support/verification-settings.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification') }, navigating: { to: null } }));

const overview: VerificationOverviewView = {
  from: '2026-11-01T00:00:00Z',
  to: '2026-11-09T12:00:00Z',
  checked: 49,
  pending: 2,
  levels: { clear: 41, review: 6, verify: 2 },
  groups: [
    { group: 'Network', label: 'Network', applications: 5, on: true },
    { group: 'Contact', label: 'Contact details', applications: 0, on: true },
    { group: 'Documents', label: 'Documents', applications: 0, on: false },
    { group: 'Answers', label: 'Answers', applications: 0, on: true },
    { group: 'Duplicates', label: 'Duplicates', applications: 3, on: true },
    { group: 'FormUse', label: 'Form activity', applications: 0, on: true },
  ],
  // Two hours before the test runs, so it always reads as time ago.
  lastCheckedAt: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString(),
};

function renderPage(result: VerificationOverviewView | Response, signedIn = user, withLimits = true, shown = limits) {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/verification/overview': () => (result instanceof Response ? result : Response.json(result)),
        'GET /v1/organization/verification-settings': () => Response.json(settingsWith({ limits: withLimits ? shown : null })),
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: { data: { user: signedIn } } } });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Overview tab', () => {
  it('shows the month, the count checked and the count at each level, with the one word of each', async () => {
    renderPage(overview);

    expect(await screen.findByText('Applications checked')).toBeInTheDocument();
    expect(screen.getByText('2 still being checked')).toBeInTheDocument();
    const stat = (title: string) => screen.getByText(title).closest('[data-slot="report-stat"]') as HTMLElement;
    expect(within(stat('Applications checked')).getByText('49')).toBeInTheDocument();
    expect(within(stat('Clear')).getByText('41')).toBeInTheDocument();
    expect(within(stat('Review')).getByText('6')).toBeInTheDocument();
    expect(within(stat('Verify')).getByText('2')).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('marks each level with its own icon, as the chips do: circle-check for Clear, eye for Review, id-card for Verify', async () => {
    renderPage(overview);

    await screen.findByText('Applications checked');
    const stat = (title: string) => screen.getByText(title).closest('[data-slot="report-stat"]') as HTMLElement;
    expect(stat('Clear').querySelector('[data-status-icon]')).not.toBeNull();
    expect(stat('Review').querySelector('[data-level-mark]')).toHaveAttribute('data-icon', 'eye');
    expect(stat('Verify').querySelector('[data-level-mark]')).toHaveAttribute('data-icon', 'id-card');
  });

  it('says when the newest check finished, as time ago with the exact time on hover (T7), and nothing before the first', async () => {
    renderPage(overview);

    const stat = (await screen.findByText('Applications checked')).closest('[data-slot="report-stat"]') as HTMLElement;
    expect(stat).toHaveTextContent(/Last checked 2 hours ago/);
    expect(stat.querySelector('time')).toHaveAttribute('datetime', overview.lastCheckedAt!);
  });

  it('leaves out when it last checked while nothing has been checked this month', async () => {
    renderPage({ ...overview, checked: 0, pending: 0, levels: { clear: 0, review: 0, verify: 0 }, groups: [], lastCheckedAt: null });

    const stat = (await screen.findByText('Applications checked')).closest('[data-slot="report-stat"]') as HTMLElement;
    expect(stat).not.toHaveTextContent(/Last checked/);
    // No empty line under the value either: nothing is pending, so the tile has no line at all.
    expect(stat.querySelector('p')).toBeNull();
  });

  it('shows a score, a percentage or an AI-assistance count nowhere', async () => {
    renderPage(overview);
    await screen.findByText('Applications checked');

    expect(document.body.textContent).not.toMatch(/%/);
    expect(document.body.textContent).not.toMatch(/score/i);
    expect(document.body.textContent).not.toMatch(/AI assistance|AI help/i);
    expect(screen.queryByRole('button', { name: /reject/i })).not.toBeInTheDocument();
  });

  it('charts all six groups in their order, with the same numbers as a table, and marks a group that is off as Off with a link to Checks', async () => {
    renderPage(overview);

    const panel = (await screen.findByRole('heading', { name: 'Findings by group, November 2026' })).closest('section') as HTMLElement;
    for (const name of ['Network', 'Contact details', 'Answers', 'Duplicates', 'Form activity']) expect(within(panel).getByText(name)).toBeInTheDocument();
    expect(within(panel).getByText('5')).toBeInTheDocument();
    const off = within(panel).getByRole('link', { name: 'Documents' });
    expect(off).toHaveAttribute('href', '/verification/checks');
    expect(within(panel).getByText('Off')).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Table' })).toBeInTheDocument();
  });

  it('says so when every group is on and found nothing', async () => {
    renderPage({ ...overview, groups: overview.groups.map((group) => ({ ...group, applications: 0, on: true })), levels: { clear: 49, review: 0, verify: 0 } });

    expect(await screen.findByText('No group of checks found anything this month.')).toBeInTheDocument();
  });

  it('says nothing about the people left over when every check has finished', async () => {
    renderPage({ ...overview, pending: 0 });
    await screen.findByText('Applications checked');

    expect(screen.queryByText(/still being checked/)).not.toBeInTheDocument();
  });

  it('shows the AI usage against the budget, with a link to AI use, for who can see usage', async () => {
    renderPage(overview);

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect(screen.getAllByText('420 tokens of 2,000,000 tokens').length).toBeGreaterThan(0);
    expect(screen.getByRole('link', { name: 'See AI use' })).toHaveAttribute('href', '/verification/ai-use');
  });

  it('shows the budget in billed dollars where the deployment prices its model, with no estimate to explain', async () => {
    renderPage(overview, user, true, dollarLimits);

    expect(await screen.findByRole('heading', { name: 'Monthly limits' })).toBeInTheDocument();
    expect((await screen.findAllByText('$0.07 of $3.84')).length).toBeGreaterThan(0);
    expect(screen.queryByText(/estimate/)).not.toBeInTheDocument();
    expect(screen.queryByText(/tokens of/)).not.toBeInTheDocument();
  });

  describe.each(['Recruiter', 'Viewer'] as const)('for a %s', (role) => {
    it('shows the counts and no AI usage', async () => {
      renderPage(overview, userAs(role), false);

      expect(await screen.findByText('Applications checked')).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Monthly limits' })).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'See AI use' })).not.toBeInTheDocument();
    });
  });

  it('shows an error with Try again when the overview does not load', async () => {
    renderPage(Response.json({ detail: 'down' }, { status: 500 }));

    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
