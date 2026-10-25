import type { CurrentUser, ScorecardTemplateDetail, TemplateUsageList } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const { pageState, goto } = vi.hoisted(() => ({
  pageState: { data: { user: undefined as CurrentUser | undefined }, url: new URL('http://dashboard.test/templates/scorecards/x') },
  goto: vi.fn(async () => {}),
}));
vi.mock('$app/state', () => ({ page: pageState, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto, beforeNavigate: () => {} }));

const own: ScorecardTemplateDetail = {
  id: 's-own',
  name: 'Technical interview',
  builtIn: false,
  copiedFrom: null,
  questions: [
    { id: 'problem', type: 'Rating5', label: 'Problem solving', required: true, guidance: 'Breaks the exercise into steps.', options: null },
    { id: 'notes', type: 'LongText', label: 'Notes on the exercise', required: false, guidance: null, options: null },
  ],
  lastUpdated: { at: '2026-10-25T09:00:00Z', by: 'Member', userId: user.id },
  archived: false,
};

const builtIn: ScorecardTemplateDetail = {
  ...own,
  id: 'screening',
  name: 'Screening',
  builtIn: true,
  lastUpdated: { at: null, by: 'ScreenHire' },
};

const usage: TemplateUsageList = {
  hiring: [],
  scorecards: [
    { templateId: 's-own', usedBy: [{ kind: 'HiringTemplate', id: 't-1', name: 'Brightwater engineering' }, { kind: 'HiringTemplate', id: 't-2', name: 'Platform' }], lastUsedAt: null, notUsedRecently: false },
    { templateId: 'screening', usedBy: [], lastUsedAt: null, notUsedRecently: false },
  ],
  emails: [],
};

function renderPage(templateId: string, routes: Parameters<typeof apiStub>[0] = {}) {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/scorecard-templates/s-own': () => Response.json(own),
        'GET /v1/scorecard-templates/screening': () => Response.json(builtIn),
        'GET /v1/template-usage': () => Response.json(usage),
        'GET /v1/organization/members': () => Response.json([]),
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: { params: { templateId } } } });
}

beforeEach(() => {
  pageState.data.user = user;
});

afterEach(() => {
  vi.unstubAllGlobals();
  goto.mockClear();
});

describe('scorecard template page', () => {
  it('edits the questions in the form builder, previews them as a reviewer fills them in, and saves', async () => {
    const saved: unknown[] = [];
    renderPage('s-own', {
      'PUT /v1/scorecard-templates/s-own': (init) => {
        const body = JSON.parse(String(init?.body)) as { name: string };
        saved.push(body);
        return Response.json({ ...own, name: body.name });
      },
    });
    const band = await screen.findByRole('heading', { level: 1, name: 'Technical interview' });
    expect(band).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: '2 hiring templates' })).toBeInTheDocument();

    await fireEvent.input(screen.getByLabelText('Name'), { target: { value: 'Technical review' } });
    expect(screen.getByRole('button', { name: 'Discard changes' })).toBeInTheDocument();
    await fireEvent.click(within(screen.getByRole('group', { name: 'View' })).getByRole('button', { name: 'Preview' }));
    expect(screen.queryByLabelText('Name')).toBeNull();
    const preview = screen.getByRole('region', { name: 'Scorecard for Jordan Lee' });
    expect(within(preview).getByText('Problem solving')).toBeInTheDocument();
    expect(within(preview).getByText('Overall recommendation')).toBeInTheDocument();
    await expectNoAxeViolations();

    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(saved).toHaveLength(1));
    expect(saved[0]).toMatchObject({ name: 'Technical review', questions: own.questions });
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it('reads a built-in only, says why, and offers Copy scorecard with no History (E3)', async () => {
    renderPage('screening');

    expect(await screen.findByText(/Copy this ScreenHire scorecard to change it/)).toBeInTheDocument();
    expect(screen.getByText('Built in')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy scorecard' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'History' })).toBeNull();
    expect(screen.queryByLabelText('Name')).toBeNull();
    expect(screen.getByRole('list', { name: 'Questions' })).toHaveTextContent('Problem solving');
  });

  it('reads only for a role that cannot change scorecards, and says so', async () => {
    pageState.data.user = userAs('Viewer');
    renderPage('s-own');

    expect(await screen.findByText(/Ask an owner, admin or recruiter to change scorecards/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'More actions' })).toBeNull();
  });
});
