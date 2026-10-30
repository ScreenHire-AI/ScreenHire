import type { CurrentUser, EmailTemplateEntry, EmailTemplateList, HiringTemplateSummary, ScorecardTemplateSummary, TemplateUsageList } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, user, userAs } from '../../../test-support/fixtures.js';
import { stubViewport } from '../../../test-support/viewport.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const { pageState, goto } = vi.hoisted(() => ({
  pageState: { data: { user: undefined as CurrentUser | undefined }, url: new URL('http://dashboard.test/templates') },
  goto: vi.fn(async () => {}),
}));
vi.mock('$app/state', () => ({ page: pageState, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto }));

const hiring = (id: string, name: string, overrides: Partial<HiringTemplateSummary> = {}): HiringTemplateSummary => ({
  id,
  name,
  description: null,
  isSystem: false,
  latestPublishedVersion: 1,
  latestPublishedVersionId: `${id}-v1`,
  draftVersion: null,
  createdAt: '2026-10-01T09:00:00Z',
  updatedAt: null,
  lastUpdated: { at: '2026-10-01T09:00:00Z', by: 'Member', userId: user.id },
  archived: false,
  ...overrides,
});

const scorecard = (id: string, name: string, overrides: Partial<ScorecardTemplateSummary> = {}): ScorecardTemplateSummary => ({
  id,
  name,
  builtIn: false,
  questions: 3,
  ratingQuestions: 2,
  lastUpdated: { at: '2026-10-02T09:00:00Z', by: 'Member', userId: user.id },
  archived: false,
  ...overrides,
});

const email = (id: string, name: string, overrides: Partial<EmailTemplateEntry> = {}): EmailTemplateEntry => ({
  id,
  name,
  builtIn: false,
  edited: false,
  rejectOnly: false,
  subject: 'Interview for {{ jobTitle }}',
  body: 'Hi {{ firstName }}',
  updatedAt: null,
  lastUpdated: { at: '2026-10-03T09:00:00Z', by: 'Member', userId: user.id },
  questionnaireOnly: false,
  archived: false,
  ...overrides,
});

const hiringTemplates = [
  hiring('t-own', 'Brightwater engineering', { description: 'How we hire engineers', draftVersion: 2 }),
  hiring('t-system', 'Hourly role', { isSystem: true, lastUpdated: { at: '2026-09-20T09:00:00Z', by: 'ScreenHire' } }),
  hiring('t-old', 'Summer interns 2025'),
  hiring('t-gone', 'Winter interns', { archived: true }),
];

const emailList: EmailTemplateList = {
  sendingEnabled: true,
  templates: [
    email('application-received', 'Application received', { builtIn: true, lastUpdated: { at: null, by: 'ScreenHire' } }),
    email('application-rejected', 'Not progressing', { builtIn: true, edited: true, rejectOnly: true, subject: 'Your application for {{ jobTitle }}' }),
    email('e-own', 'Interview invitation'),
  ],
  variables: [{ name: 'jobTitle', label: 'Job title', rejectOnly: false }],
};

const usage: TemplateUsageList = {
  hiring: [
    { templateId: 't-own', usedBy: [{ kind: 'Job', id: 'j-1', name: 'Senior .NET Engineer' }, { kind: 'Job', id: 'j-2', name: 'Platform Engineer' }], lastUsedAt: '2026-10-20T09:00:00Z', notUsedRecently: false },
    { templateId: 't-system', usedBy: [{ kind: 'Job', id: 'j-3', name: 'Barista' }], lastUsedAt: '2026-10-10T09:00:00Z', notUsedRecently: false },
    { templateId: 't-old', usedBy: [], lastUsedAt: '2025-06-02T09:00:00Z', notUsedRecently: true },
    { templateId: 't-gone', usedBy: [], lastUsedAt: null, notUsedRecently: false },
  ],
  scorecards: [{ templateId: 's-own', usedBy: [], lastUsedAt: null, notUsedRecently: false }],
  emails: [
    { templateId: 'application-received', usedBy: [], lastUsedAt: null, notUsedRecently: false },
    { templateId: 'application-rejected', usedBy: [], lastUsedAt: null, notUsedRecently: false },
    { templateId: 'e-own', usedBy: [{ kind: 'JobAutomation', id: 'j-1', name: 'Senior .NET Engineer', automationId: 'a-1', automationName: 'Invite' }], lastUsedAt: null, notUsedRecently: false },
  ],
};

function renderPage(path = '/templates', routes: Parameters<typeof apiStub>[0] = {}) {
  pageState.url = new URL(`http://dashboard.test${path}`);
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/templates': () => Response.json(hiringTemplates),
      'GET /v1/scorecard-templates': () => Response.json([scorecard('screening', 'Screening', { builtIn: true, lastUpdated: { at: null, by: 'ScreenHire' } }), scorecard('s-own', 'Technical interview')]),
      'GET /v1/email-templates': () => Response.json(emailList),
      'GET /v1/template-usage': () => Response.json(usage),
      'GET /v1/organization/members': () => Response.json([]),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return fetchMock;
}

beforeEach(() => {
  pageState.data.user = user;
});

afterEach(() => {
  vi.unstubAllGlobals();
  goto.mockClear();
});

describe('templates page', () => {
  it('lists the hiring templates with Built in, Used by, the draft and who updated each, and counts each tab without the archived', async () => {
    renderPage();
    const table = await screen.findByRole('table', { name: 'Hiring templates' });
    const own = await within(table).findByRole('row', { name: /Brightwater engineering/ });

    await waitFor(() => expect(own).toHaveTextContent('2 jobs'));
    // The draft has its own column, not a badge beside the version.
    expect(own).toHaveTextContent('Version 2');
    expect(own).toHaveTextContent('by you');
    expect(within(table).getByRole('row', { name: /Hourly role/ })).toHaveTextContent('Built in');
    expect(within(table).getByRole('row', { name: /Hourly role/ })).toHaveTextContent('by ScreenHire');
    expect(within(table).queryByText('Winter interns')).toBeNull();
    const tabs = screen.getByRole('tablist', { name: 'Kinds of template' });
    expect(within(tabs).getByRole('tab', { name: /Hiring/ })).toHaveTextContent('3');
    expect(within(tabs).getByRole('tab', { name: /Scorecards/ })).toHaveTextContent('2');
    expect(within(tabs).getByRole('tab', { name: /Emails/ })).toHaveTextContent('3');
    await expectNoAxeViolations();
  });

  it('filters by In use, Not used in 6 months and Archived, each with its count, and searches', async () => {
    renderPage();
    const table = await screen.findByRole('table', { name: 'Hiring templates' });
    const filters = screen.getByRole('group', { name: 'Show' });
    await waitFor(() => expect(within(filters).getByRole('button', { name: /In use/ })).toHaveTextContent('2'));
    expect(within(filters).getByRole('button', { name: /Not used in 6 months/ })).toHaveTextContent('1');
    expect(within(filters).getByRole('button', { name: /Archived/ })).toHaveTextContent('1');

    await fireEvent.click(within(filters).getByRole('button', { name: /Not used in 6 months/ }));
    expect(within(table).getAllByRole('row')).toHaveLength(2);
    expect(within(table).getByText('Summer interns 2025')).toBeInTheDocument();

    await fireEvent.click(within(filters).getByRole('button', { name: /Archived/ }));
    expect(within(screen.getByRole('table', { name: 'Hiring templates' })).getByText('Winter interns')).toBeInTheDocument();

    await fireEvent.click(within(filters).getByRole('button', { name: /All/ }));
    await fireEvent.input(screen.getByRole('searchbox', { name: 'Search hiring templates' }), { target: { value: 'nothing like this' } });
    expect(screen.getByText('No templates match')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Show all' }));
    expect(await screen.findByText('Hourly role')).toBeInTheDocument();
  });

  it('archives your own template from its row menu without a confirm, and offers built-ins Duplicate only', async () => {
    const archived: string[] = [];
    renderPage('/templates', {
      'POST /v1/templates/t-own/archive': () => {
        archived.push('t-own');
        return Response.json({});
      },
    });
    const table = await screen.findByRole('table', { name: 'Hiring templates' });

    await fireEvent.click(await within(table).findByRole('button', { name: 'Actions for Hourly role' }));
    expect(await screen.findByRole('menuitem', { name: 'Duplicate' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Archive' })).toBeNull();
    await fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

    await fireEvent.click(within(table).getByRole('button', { name: 'Actions for Brightwater engineering' }));
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }));

    await waitFor(() => expect(archived).toEqual(['t-own']));
    expect(await screen.findByText(/Archived Brightwater engineering/)).toBeInTheDocument();
  });

  it('shows each subject variable as a word and resets an edited built-in email after a confirm', async () => {
    const reset: string[] = [];
    renderPage('/templates?tab=emails', {
      'DELETE /v1/email-templates/application-rejected': () => {
        reset.push('application-rejected');
        return new Response(null, { status: 204 });
      },
    });
    const table = await screen.findByRole('table', { name: 'Email templates' });
    const rejected = await within(table).findByRole('row', { name: /Not progressing/ });
    expect(rejected).toHaveTextContent('Your application for job title');
    await waitFor(() => expect(within(table).getByRole('row', { name: /Interview invitation/ })).toHaveTextContent('1 automation'));

    await fireEvent.click(within(rejected).getByRole('button', { name: 'Actions for Not progressing' }));
    expect(screen.queryByRole('menuitem', { name: 'Duplicate' })).toBeNull();
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Reset to original' }));
    const dialog = await screen.findByRole('dialog', { name: 'Reset Not progressing to the original?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Reset to original' }));

    await waitFor(() => expect(reset).toEqual(['application-rejected']));
  });

  it('gives a Recruiter the hiring and scorecard actions but none for emails', async () => {
    pageState.data.user = userAs('Recruiter');
    renderPage('/templates?tab=emails');
    const table = await screen.findByRole('table', { name: 'Email templates' });
    await within(table).findByText('Interview invitation');

    expect(within(table).queryByRole('button', { name: /^Actions for/ })).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'New template' }));
    expect(await screen.findByRole('menuitem', { name: 'Hiring template' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Scorecard' })).toBeInTheDocument();
    expect(screen.queryByRole('menuitem', { name: 'Email template' })).toBeNull();
  });

  it('gives a Viewer no New template and no row menus', async () => {
    pageState.data.user = userAs('Viewer');
    renderPage();
    const table = await screen.findByRole('table', { name: 'Hiring templates' });
    await within(table).findByText('Hourly role');

    expect(screen.queryByRole('button', { name: 'New template' })).toBeNull();
    expect(within(table).queryByRole('button', { name: /^Actions for/ })).toBeNull();
  });

  it('puts New template in the action bar on a phone', async () => {
    stubViewport(360);
    renderPage();
    await screen.findAllByText('Brightwater engineering');

    const buttons = screen.getAllByRole('button', { name: 'New template' });
    expect(buttons).toHaveLength(1);
    expect(buttons[0]!.closest('[data-slot=action-bar]')).not.toBeNull();
  });
});
