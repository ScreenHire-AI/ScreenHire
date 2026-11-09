import type { VerificationOverviewView } from '@screenhire/api-client';
import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../test-support/fixtures.js';
import { limits, settingsWith } from '../../../test-support/verification-settings.js';
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
    { group: 'Network', label: 'Network', applications: 5 },
    { group: 'Duplicates', label: 'Duplicates', applications: 3 },
  ],
};

function renderPage(result: VerificationOverviewView | Response, signedIn = user, withLimits = true) {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/verification/overview': () => (result instanceof Response ? result : Response.json(result)),
        'GET /v1/organization/verification-settings': () => Response.json(settingsWith({ limits: withLimits ? limits : null })),
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

  it('marks each level with its own icon, as the chips do: the circle-check for Clear, the level mark for Review and Verify', async () => {
    renderPage(overview);

    await screen.findByText('Applications checked');
    const stat = (title: string) => screen.getByText(title).closest('[data-slot="report-stat"]') as HTMLElement;
    expect(stat('Clear').querySelector('[data-status-icon]')).not.toBeNull();
    expect(stat('Review').querySelector('[data-level-mark="2"]')).not.toBeNull();
    expect(stat('Verify').querySelector('[data-level-mark="3"]')).not.toBeNull();
  });

  it('shows a score, a percentage or an AI-assistance count nowhere', async () => {
    renderPage(overview);
    await screen.findByText('Applications checked');

    expect(document.body.textContent).not.toMatch(/%/);
    expect(document.body.textContent).not.toMatch(/score/i);
    expect(document.body.textContent).not.toMatch(/AI assistance|AI help/i);
    expect(screen.queryByRole('button', { name: /reject/i })).not.toBeInTheDocument();
  });

  it('charts the groups that found something most, with the same numbers as a table', async () => {
    renderPage(overview);

    const panel = (await screen.findByRole('heading', { name: 'Findings by group, November 2026' })).closest('section') as HTMLElement;
    expect(within(panel).getByText('Network')).toBeInTheDocument();
    expect(within(panel).getByText('5')).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'Table' })).toBeInTheDocument();
  });

  it('says so when no group found anything', async () => {
    renderPage({ ...overview, groups: [], levels: { clear: 49, review: 0, verify: 0 } });

    expect(await screen.findByText('No group of checks found anything this month.')).toBeInTheDocument();
  });

  it('says nothing about the people left over when every check has finished', async () => {
    renderPage({ ...overview, pending: 0 });
    await screen.findByText('Applications checked');

    expect(screen.queryByText(/still being checked/)).not.toBeInTheDocument();
  });

  it('shows the AI usage against the budget, with a link to Usage, for who can see usage', async () => {
    renderPage(overview);

    expect(await screen.findByRole('heading', { name: 'AI budget this month' })).toBeInTheDocument();
    expect(screen.getAllByText('420 tokens of 2,000,000 tokens').length).toBeGreaterThan(0);
    expect(screen.getByRole('link', { name: 'See usage' })).toHaveAttribute('href', '/verification/usage');
  });

  describe.each(['Recruiter', 'Viewer'] as const)('for a %s', (role) => {
    it('shows the counts and no AI usage', async () => {
      renderPage(overview, userAs(role), false);

      expect(await screen.findByText('Applications checked')).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'AI budget this month' })).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'See usage' })).not.toBeInTheDocument();
    });
  });

  it('shows an error with Try again when the overview does not load', async () => {
    renderPage(Response.json({ detail: 'down' }, { status: 500 }));

    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
