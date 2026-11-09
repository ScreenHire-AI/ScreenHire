import type { VerificationActivityPage } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/verification/activity') }, navigating: { to: null } }));

const run: VerificationActivityPage['entries'][number] = {
  id: '01a157d4-0000-7000-8000-000000000001',
  kind: 'CheckRun',
  at: '2026-11-09T10:00:00Z',
  group: 'Network',
  check: 'network',
  outcome: 'Found',
  note: null,
  applicationId: '01a157d4-0000-7000-8000-0000000000a1',
  jobTitle: 'Product designer',
  call: null,
};
const off: VerificationActivityPage['entries'][number] = {
  ...run,
  id: '01a157d4-0000-7000-8000-000000000002',
  at: '2026-11-09T09:00:00Z',
  group: 'Documents',
  check: 'documents',
  outcome: 'NotRun',
  note: 'Documents checks are turned off in this organization\'s settings.',
};
const call: VerificationActivityPage['entries'][number] = {
  ...run,
  id: '01a157d4-0000-7000-8000-000000000003',
  kind: 'ModelCall',
  at: '2026-11-09T08:00:00Z',
  group: 'Answers',
  check: 'consistency.extraction',
  outcome: 'Completed',
  call: { keySource: 'BuiltIn', provider: 'Anthropic', model: 'claude-test', providerRequestId: 'req_01ABC', inputTokens: 1200, outputTokens: 300, cost: 0.0123, durationMs: 800, errorKind: null },
};

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

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the Activity log tab', () => {
  it('lists the checks and model calls newest first, with cost and the provider request id, for who can see usage', async () => {
    renderPage({ 'limit=50': { entries: [run, off, call], nextBefore: null, usageVisible: true } });

    const table = within(await screen.findByRole('region', { name: 'Activity table' })).getByRole('table');
    await within(table).findByText('Network address');
    const rows = within(table).getAllByRole('row').slice(1);
    expect(rows).toHaveLength(3);
    expect(within(rows[0]).getByText('Found something')).toBeInTheDocument();
    expect(within(rows[0]).getByRole('link', { name: 'Product designer' })).toHaveAttribute('href', '/applications/01a157d4-0000-7000-8000-0000000000a1');
    expect(within(rows[1]).getByText('Did not run')).toBeInTheDocument();
    expect(within(rows[1]).getByText(/turned off in this organization/)).toBeInTheDocument();
    expect(within(rows[2]).getByText('Consistency, reading a source')).toBeInTheDocument();
    expect(within(rows[2]).getByText('req_01ABC')).toBeInTheDocument();
    expect(within(rows[2]).getByText('claude-test')).toBeInTheDocument();
    expect(within(rows[2]).getByText("ScreenHire's key")).toBeInTheDocument();
    expect(within(rows[2]).getByText('$0.0123')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Export CSV/ })).toHaveAttribute('href', '/v1/verification/activity/export');
    await expectNoAxeViolations();
  });

  describe.each(['Recruiter', 'Viewer'] as const)('for a %s', (role) => {
    it('leaves out the model, key, request id, tokens, cost and the export', async () => {
      renderPage({ 'limit=50': { entries: [run, off], nextBefore: null, usageVisible: false } }, userAs(role));

      const table = within(await screen.findByRole('region', { name: 'Activity table' })).getByRole('table');
      await within(table).findByText('Network address');
      expect(within(table).queryByRole('columnheader', { name: 'Provider request id' })).not.toBeInTheDocument();
      expect(within(table).queryByRole('columnheader', { name: 'Cost' })).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: /Export CSV/ })).not.toBeInTheDocument();
      expect(screen.queryByRole('combobox', { name: 'Filter by key' })).not.toBeInTheDocument();
      expect(document.querySelectorAll('[disabled]')).toHaveLength(0);
      await expectNoAxeViolations();
    });
  });

  it('filters by group, key and outcome, and asks the server for them', async () => {
    const empty: VerificationActivityPage = { entries: [], nextBefore: null, usageVisible: true };
    const requests = renderPage({
      'limit=50': { entries: [run], nextBefore: null, usageVisible: true },
      'group=Answers&keySource=OwnKey&outcome=Failed&limit=50': empty,
    });
    await screen.findByText('Network address');

    await fireEvent.change(screen.getByRole('combobox', { name: 'Filter by group' }), { target: { value: 'Answers' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Filter by key' }), { target: { value: 'OwnKey' } });
    await fireEvent.change(screen.getByRole('combobox', { name: 'Filter by outcome' }), { target: { value: 'Failed' } });

    expect(await screen.findByText('No activity matches.')).toBeInTheDocument();
    expect(requests).toContain('/v1/verification/activity?group=Answers&keySource=OwnKey&outcome=Failed&limit=50');
    expect(screen.getByRole('link', { name: /Export CSV/ })).toHaveAttribute('href', '/v1/verification/activity/export?group=Answers&keySource=OwnKey&outcome=Failed');
  });

  it('shows more with the cursor of the page above', async () => {
    const requests = renderPage({
      'limit=50': { entries: [run], nextBefore: 'cursor-1', usageVisible: false },
      'before=cursor-1&limit=50': { entries: [off], nextBefore: null, usageVisible: false },
    }, userAs('Recruiter'));
    await screen.findByText('Network address');

    await fireEvent.click(screen.getByRole('button', { name: 'Show more' }));

    await waitFor(() => expect(screen.getByText('Documents')).toBeInTheDocument());
    expect(requests).toContain('/v1/verification/activity?before=cursor-1&limit=50');
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument();
  });
});
