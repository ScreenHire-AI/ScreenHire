import type { VerificationActivityCheck, VerificationActivityPage, VerificationActivityRun } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/activity') }, navigating: { to: null } }));

const check = (overrides: Partial<VerificationActivityCheck>): VerificationActivityCheck => ({
  id: '01a157d4-0000-7000-8000-000000000001',
  kind: 'CheckRun',
  at: '2026-11-09T10:00:00Z',
  group: 'Network',
  check: 'network',
  outcome: 'Completed',
  reason: null,
  findings: [],
  call: null,
  ...overrides,
});

const call = check({
  id: '01a157d4-0000-7000-8000-000000000009',
  kind: 'ModelCall',
  group: 'Answers',
  check: 'consistency.extraction',
  outcome: 'Completed',
  call: { keySource: 'BuiltIn', provider: 'Anthropic', model: 'claude-test', providerRequestId: 'req_01ABC', inputTokens: 1200, outputTokens: 300, cost: 0.0123, durationMs: 800, errorKind: null },
});

const run: VerificationActivityRun = {
  id: '638664400000000000.01a157d40000700080000000000000a1',
  at: '2026-11-09T10:00:00Z',
  applicationId: '01a157d4-0000-7000-8000-0000000000a1',
  jobTitle: 'Frontend Engineer (Svelte)',
  outcome: 'Found',
  checks: 3,
  found: 1,
  notRun: 1,
  rulesetVersion: '2026.11.4',
  items: [
    check({ outcome: 'Found', findings: ['Sent from a hosting provider'] }),
    check({
      id: '01a157d4-0000-7000-8000-000000000002',
      group: 'Documents',
      check: 'documents',
      outcome: 'NotRun',
      reason: "Documents checks are turned off in this organization's settings.",
    }),
    check({ id: '01a157d4-0000-7000-8000-000000000003', group: 'Answers', check: 'consistency' }),
    call,
  ],
};
const quiet: VerificationActivityRun = {
  ...run,
  id: '638664364000000000.01a157d40000700080000000000000a2',
  at: '2026-11-09T09:00:00Z',
  applicationId: '01a157d4-0000-7000-8000-0000000000a2',
  jobTitle: 'Support Engineer',
  outcome: 'Completed',
  checks: 2,
  found: 0,
  notRun: 0,
  items: [check({ id: '01a157d4-0000-7000-8000-000000000004' }), check({ id: '01a157d4-0000-7000-8000-000000000005', group: 'Contact', check: 'identity' })],
};
const withoutCallDetails = (page: VerificationActivityRun): VerificationActivityRun => ({ ...page, items: page.items.map((item) => ({ ...item, call: null })) });

function renderPage(pages: Record<string, VerificationActivityPage>, signedIn = user) {
  const requests: string[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      requests.push(url);
      const key = url.split('?')[1] ?? '';
      const page = pages[key] ?? pages[''];
      return apiStub({ [`GET ${url}`]: () => Response.json(page) })(input, init);
    }),
  );
  render(WithQueryClient, { props: { component: Page, props: { data: { user: signedIn } } } });
  return requests;
}

const table = async () => within(await screen.findByRole('region', { name: 'Activity table' })).getByRole('table');
const runButton = (name: RegExp | string) => screen.findByRole('button', { name });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Activity log tab', () => {
  it('lists one row per run, newest first, its outcome a status and nothing else, with the six detail columns gone', async () => {
    renderPage({ 'limit=50': { runs: [run, quiet], nextBefore: null, usageVisible: true } });

    const grid = await table();
    await within(grid).findByText('Frontend Engineer (Svelte), 3 checks, 1 did not run');
    expect(within(grid).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual(['When', 'Run', 'Outcome']);
    const rows = within(grid).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(rows[0]!.querySelectorAll('td')[2]!.textContent?.trim()).toBe('Found');
    expect(rows[1]!.querySelectorAll('td')[2]!.textContent?.trim()).toBe('Nothing found');
    expect(within(rows[1]!).getByText('Support Engineer, 2 checks')).toBeInTheDocument();
    // A run that found something keeps the review fill; a calm one is quiet (T3).
    expect(within(rows[0]!).getByText('Found').closest('[data-slot="badge"]')).toHaveClass('bg-status-review-soft');
    expect(screen.getByRole('link', { name: /Export CSV/ })).toHaveAttribute('href', '/v1/verification/activity/export');
    await expectNoAxeViolations();
  });

  it("opens a run to its checks: why one did not run, what one found, the AI check's call and the ruleset, never a threshold or a rule", async () => {
    renderPage({ 'limit=50': { runs: [run], nextBefore: null, usageVisible: true } });

    const button = await runButton(/Frontend Engineer \(Svelte\), 3 checks, 1 did not run/);
    expect(button).toHaveAttribute('aria-expanded', 'false');
    await fireEvent.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'true');

    const detail = document.getElementById(button.getAttribute('aria-controls')!)!;
    const checks = within(detail).getByRole('list', { name: /^Checks in / });
    const network = checks.querySelector<HTMLElement>('[data-check="network"]')!;
    expect(network).toHaveTextContent('Network address');
    expect(within(network).getByRole('list', { name: 'What it found' })).toHaveTextContent('Sent from a hosting provider');
    expect(checks.querySelector('[data-check="documents"]')).toHaveTextContent("Did not run Documents checks are turned off in this organization's settings.");
    const ai = checks.querySelector<HTMLElement>('[data-check="consistency.extraction"]')!;
    expect(ai).toHaveTextContent('Answered');
    expect(within(ai).getByText('req_01ABC')).toBeInTheDocument();
    expect(within(ai).getByText('claude-test')).toBeInTheDocument();
    expect(within(ai).getByText("ScreenHire's key")).toBeInTheDocument();
    expect(within(ai).getByText('$0.01')).toBeInTheDocument();
    expect(ai).toHaveTextContent('1,200 in, 300 out');
    expect(within(detail).getByRole('link', { name: 'Open application' })).toHaveAttribute('href', '/applications/01a157d4-0000-7000-8000-0000000000a1');
    expect(detail).toHaveTextContent('Ruleset 2026.11.4');
    expect(detail.textContent).not.toMatch(/threshold|weight|rule [A-Z]|score|%/i);
    await expectNoAxeViolations();

    await fireEvent.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(document.getElementById(detail.id)).toBeNull();
  });

  describe.each(['Recruiter', 'Viewer'] as const)('for a %s', (role) => {
    it('leaves out the call details, the key filter and the export', async () => {
      renderPage({ 'limit=50': { runs: [withoutCallDetails(run)], nextBefore: null, usageVisible: false } }, userAs(role));

      await fireEvent.click(await runButton(/Frontend Engineer/));
      expect(document.querySelector('[data-call]')).toBeNull();
      expect(screen.queryByText('req_01ABC')).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: /Export CSV/ })).not.toBeInTheDocument();
      expect(screen.queryByRole('combobox', { name: 'Filter by key' })).not.toBeInTheDocument();
      expect(document.querySelectorAll('[disabled]')).toHaveLength(0);
      await expectNoAxeViolations();
    });
  });

  it('filters by group, key and the three outcomes, and asks the server for them', async () => {
    const empty: VerificationActivityPage = { runs: [], nextBefore: null, usageVisible: true };
    const requests = renderPage({
      'limit=50': { runs: [run], nextBefore: null, usageVisible: true },
      'group=Answers&keySource=OwnKey&outcome=NotRun&limit=50': empty,
    });
    await runButton(/Frontend Engineer/);

    const outcome = screen.getByRole('combobox', { name: 'Filter by outcome' });
    expect(within(outcome).getAllByRole('option').map((option) => option.textContent)).toEqual(['Any outcome', 'Found', 'Nothing found', 'Did not run']);
    await fireEvent.change(screen.getByRole('combobox', { name: 'Filter by group' }), { target: { value: 'Answers' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Filter by key' }), { target: { value: 'OwnKey' } });
    await fireEvent.change(outcome, { target: { value: 'NotRun' } });

    expect(await screen.findByText('No activity matches.')).toBeInTheDocument();
    expect(requests).toContain('/v1/verification/activity?group=Answers&keySource=OwnKey&outcome=NotRun&limit=50');
    expect(screen.getByRole('link', { name: /Export CSV/ })).toHaveAttribute('href', '/v1/verification/activity/export?group=Answers&keySource=OwnKey&outcome=NotRun');
  });

  it('shows more with the cursor of the page above', async () => {
    const requests = renderPage(
      {
        'limit=50': { runs: [withoutCallDetails(run)], nextBefore: 'cursor-1', usageVisible: false },
        'before=cursor-1&limit=50': { runs: [quiet], nextBefore: null, usageVisible: false },
      },
      userAs('Recruiter'),
    );
    await runButton(/Frontend Engineer/);

    await fireEvent.click(screen.getByRole('button', { name: 'Show more' }));

    await waitFor(() => expect(screen.getByText('Support Engineer, 2 checks')).toBeInTheDocument());
    expect(requests).toContain('/v1/verification/activity?before=cursor-1&limit=50');
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument();
  });
});
