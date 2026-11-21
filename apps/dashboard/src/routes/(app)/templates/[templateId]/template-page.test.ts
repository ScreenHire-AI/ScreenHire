// A hiring template's draft: Save draft always works, even with problems, and Publish waits until the workflow is valid.
import type { CurrentUser, DefinitionError, HiringTemplateDetail, HiringTemplateVersionDetail, HiringWorkflow, TemplateUsageList } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub, user, userAs } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

const { pageState, goto } = vi.hoisted(() => ({
  pageState: { data: { user: undefined as CurrentUser | undefined }, url: new URL('http://dashboard.test/templates/t-1?tab=pipeline') },
  goto: vi.fn(async (..._args: unknown[]) => {}),
}));
vi.mock('$app/state', () => ({ page: pageState, navigating: { to: null } }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto }));

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone-screen', name: 'Phone screen', kind: 'Screening' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: { questions: [] },
  // A step with no tag: the API's validator reports it, but a draft may still be saved.
  automations: [{ id: 'tag-screen', name: 'Tag screened', trigger: { type: 'StageEntered', stageId: 'phone-screen' }, conditions: null, actions: [{ type: 'AddTag', tag: '' }] }],
};

const detail: HiringTemplateDetail = {
  id: 't-1',
  name: 'Brightwater engineering',
  description: null,
  isSystem: false,
  latestPublishedVersion: null,
  createdAt: '2026-10-01T09:00:00Z',
  updatedAt: null,
  versions: [{ id: 'v-1', version: 1, status: 'Draft', createdAt: '2026-10-01T09:00:00Z', publishedAt: null }] as HiringTemplateDetail['versions'],
  lastUpdated: { at: '2026-10-01T09:00:00Z', by: 'Member', userId: user.id },
  archived: false,
};

const version = (): HiringTemplateVersionDetail =>
  ({
    id: 'v-1',
    templateId: 't-1',
    version: 1,
    status: 'Draft',
    definition: { workflow: structuredClone(workflow) },
    createdAt: '2026-10-01T09:00:00Z',
    publishedAt: null,
    publishedBy: null,
    problems: [],
  }) as unknown as HiringTemplateVersionDetail;

let problems: DefinitionError[] = [];

const noUsage: TemplateUsageList = { hiring: [], scorecards: [], emails: [] };

function renderPage(routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/templates/t-1': () => Response.json(detail),
      'GET /v1/templates/t-1/versions/1': () => Response.json(version()),
      'GET /v1/template-usage': () => Response.json(noUsage),
      'GET /v1/organization/members': () => Response.json([]),
      'GET /v1/organization/verification-settings': () => Response.json({ defaultAiUse: 'NotAllowed' }),
      'GET /v1/scorecard-templates': () => Response.json([]),
      'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false, variables: [] }),
      'POST /v1/workflows/validate': () => Response.json({ valid: problems.length === 0, errors: problems }),
      'PUT /v1/templates/t-1/versions/1': (init) => Response.json({ ...version(), definition: JSON.parse(String(init?.body)).definition }),
      'POST /v1/templates/t-1/versions/1/publish': () => Response.json({ ...version(), status: 'Published' }),
      ...routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: Page, props: { params: { templateId: 't-1' } } } });
  return fetchMock;
}

const calls = (fetchMock: ReturnType<typeof vi.fn>, method: string, url: string) =>
  fetchMock.mock.calls.filter(([path, init]) => path === url && (init?.method ?? 'GET') === method);

// An edit that is not a fix: a stage added after the first, which opens its sheet.
async function addAStage() {
  await fireEvent.click(await screen.findByRole('button', { name: 'Add a stage after Applied' }));
  await fireEvent.click(await screen.findByRole('button', { name: 'Done' }));
}

beforeEach(() => {
  // jsdom has no Web Animations, and a card entering the list's `animate:flip` asks for its running ones.
  Element.prototype.getAnimations = () => [];
  pageState.data.user = user;
  pageState.url = new URL('http://dashboard.test/templates/t-1?tab=pipeline');
  problems = [{ path: 'automations[0].actions[0]', message: 'The tag in "Tag screened" has no name. Type one, or remove that step.' }];
});

afterEach(() => {
  delete (Element.prototype as Partial<Element>).getAnimations;
  vi.unstubAllGlobals();
  goto.mockClear();
});

describe('template page, the AI-use question', () => {
  it('shows the organization policy as a locked row on the Questions tab, with a link to change it for someone who may', async () => {
    pageState.url = new URL('http://dashboard.test/templates/t-1?tab=questions');
    renderPage();

    const row = await waitFor(() => {
      const found = document.querySelector<HTMLElement>('[data-ai-use-row]');
      expect(found).toHaveTextContent('Policy: Not allowed.');
      return found!;
    });
    expect(row).toHaveTextContent('AI use. Asked by ScreenHire.');
    expect(within(row).getByRole('link', { name: 'Change policy' })).toHaveAttribute('href', '/verification/policy');
    expect(screen.queryByRole('combobox', { name: 'AI use while applying' })).toBeNull();
  });

  it('leaves the link out for a role that cannot change settings (A3)', async () => {
    pageState.data.user = userAs('Recruiter');
    pageState.url = new URL('http://dashboard.test/templates/t-1?tab=questions');
    renderPage();

    await waitFor(() => expect(document.querySelector('[data-ai-use-row]')).toHaveTextContent('Policy: Not allowed.'));
    expect(screen.queryByRole('link', { name: 'Change policy' })).toBeNull();
  });
});

describe('template page, a draft with problems', () => {
  it('saves the draft anyway, and says it saved', async () => {
    const fetchMock = renderPage();
    await addAStage();

    await fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

    await waitFor(() => expect(calls(fetchMock, 'PUT', '/v1/templates/t-1/versions/1')).toHaveLength(1));
    const sentWorkflow = JSON.parse(String(calls(fetchMock, 'PUT', '/v1/templates/t-1/versions/1')[0]![1].body)).definition.workflow as HiringWorkflow;
    expect(sentWorkflow.stages.map((stage) => stage.name)).toEqual(['Applied', 'New stage', 'Phone screen', 'Hired', 'Not progressing']);
    expect(sentWorkflow.automations[0]!.actions[0]).toEqual({ type: 'AddTag', tag: '' });
    expect(await screen.findByText('Saved version 1.')).toBeInTheDocument();
  });

  it('shows Needs fixes with a Fix button, and keeps Publish disabled until the pipeline is valid', async () => {
    renderPage();

    const publish = await screen.findByRole('button', { name: 'Publish' });
    await screen.findByText('Needs fixes');
    expect(screen.getByRole('button', { name: 'Fix 1 thing' })).toBeInTheDocument();
    expect(publish).toBeDisabled();
  });

  it('enables Publish once the validator finds nothing, and Publish saves the draft first and then publishes it', async () => {
    problems = [];
    const fetchMock = renderPage();
    const publish = await screen.findByRole('button', { name: 'Publish' });
    await waitFor(() => expect(publish).toBeEnabled());
    expect(screen.queryByText('Needs fixes')).toBeNull();

    await addAStage();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish' })).toBeEnabled());
    await fireEvent.click(screen.getByRole('button', { name: 'Publish' }));

    await waitFor(() => expect(calls(fetchMock, 'POST', '/v1/templates/t-1/versions/1/publish')).toHaveLength(1));
    expect(calls(fetchMock, 'PUT', '/v1/templates/t-1/versions/1')).toHaveLength(1);
  });

  it('turns Publish off again the moment an edit is made, until that edit is checked', async () => {
    problems = [];
    renderPage();
    const publish = await screen.findByRole('button', { name: 'Publish' });
    await waitFor(() => expect(publish).toBeEnabled());

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a stage after Applied' }));

    expect(screen.getByRole('button', { name: 'Publish' })).toBeDisabled();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish' })).toBeEnabled());
  });
});

// Version 2 is published and version 3 is a draft; one job is on version 1, one on version 2.
const published: HiringTemplateDetail = {
  ...detail,
  latestPublishedVersion: 2,
  versions: [
    { id: 'v-1', version: 1, status: 'Published', createdAt: '2026-10-01T09:00:00Z', publishedAt: '2026-10-01T10:00:00Z' },
    { id: 'v-2', version: 2, status: 'Published', createdAt: '2026-10-05T09:00:00Z', publishedAt: '2026-10-05T10:00:00Z' },
    { id: 'v-3', version: 3, status: 'Draft', createdAt: '2026-10-09T09:00:00Z', publishedAt: null },
  ] as HiringTemplateDetail['versions'],
};

const usage: TemplateUsageList = {
  hiring: [
    {
      templateId: 't-1',
      usedBy: [
        { kind: 'Job', id: 'j-old', name: 'Barista', templateVersion: 1, workflowVersion: 4 },
        { kind: 'Job', id: 'j-new', name: 'Line cook', templateVersion: 2, workflowVersion: 1 },
      ],
      lastUsedAt: null,
      notUsedRecently: false,
    },
  ],
  scorecards: [],
  emails: [],
};

const versionOf = (number: number, status: 'Draft' | 'Published') => () =>
  Response.json({ ...version(), id: `v-${number}`, version: number, status, publishedAt: status === 'Published' ? '2026-10-05T10:00:00Z' : null });

function renderPublished(url: string, routes: Parameters<typeof apiStub>[0] = {}) {
  problems = [];
  pageState.url = new URL(url);
  return renderPage({
    'GET /v1/templates/t-1': () => Response.json(published),
    'GET /v1/templates/t-1/versions/2': versionOf(2, 'Published'),
    'GET /v1/templates/t-1/versions/3': versionOf(3, 'Draft'),
    'GET /v1/template-usage': () => Response.json(usage),
    ...routes,
  });
}

const header = () => document.querySelector<HTMLElement>('[data-slot=header-band]')!;
const fact = (label: string) => within(header()).getByText(label, { selector: 'dt' }).nextElementSibling as HTMLElement;

describe('template page, the header', () => {
  it('says the shown version\'s status and the live version as two facts, and Used by as a link with the jobs on an older version', async () => {
    renderPublished('http://dashboard.test/templates/t-1?tab=pipeline');

    await screen.findByRole('button', { name: 'Publish' });
    const terms = within(header()).getAllByRole('term').map((term) => term.textContent);
    expect(terms).toEqual(expect.arrayContaining(['Status', 'Live version', 'Used by']));
    expect(terms).not.toContain('Version');
    expect(fact('Status')).toHaveTextContent('Draft, unpublished changes');
    expect(fact('Live version')).toHaveTextContent('Version 2');
    const usedBy = await within(header()).findByRole('button', { name: '2 jobs, 1 on an older version' });
    expect(usedBy).not.toHaveAttribute('data-slot', 'button');
  });

  it('says what publishing does beside Publish: new jobs get the version, and the jobs keep theirs until updated', async () => {
    renderPublished('http://dashboard.test/templates/t-1?tab=pipeline');

    await waitFor(() =>
      expect(document.querySelector('[data-publish-line]')).toHaveTextContent(
        'Publishing makes version 3 the version new jobs get. Your 2 jobs stay on the versions they have until you update them.',
      ),
    );
  });

  it('opens Versions and History as one sheet, Versions from its button and All changes from History', async () => {
    renderPublished('http://dashboard.test/templates/t-1?tab=pipeline', {
      'GET /v1/templates/t-1/history': () => Response.json({ entries: [], hasMore: false }),
    });

    await fireEvent.click(await screen.findByRole('button', { name: 'Versions' }));
    const sheet = await screen.findByRole('dialog', { name: 'Versions and changes' });
    const list = within(sheet).getByRole('list', { name: 'Versions' });
    expect(within(list).getAllByRole('link').map((link) => link.getAttribute('href'))).toEqual([
      '/templates/t-1?version=3',
      '/templates/t-1?version=2',
      '/templates/t-1?version=1',
    ]);
    expect(within(list).getByRole('link', { name: /Version 2 \(live\)/ })).toBeInTheDocument();
    expect(within(sheet).getByRole('tab', { name: 'All changes' })).toBeInTheDocument();
  });
});

describe('template page, jobs on an older version', () => {
  it('lists each job on an older version under Review updates, with See what changes and Update', async () => {
    const applied: unknown[] = [];
    renderPublished('http://dashboard.test/templates/t-1?version=2&tab=pipeline', {
      'GET /v1/jobs/j-old/workflow/template-diff': () =>
        Response.json({ templateId: 't-1', sourceVersionId: 'v-1', sourceVersion: 1, latestVersionId: 'v-2', latestVersion: 2, updateAvailable: true, changes: [] }),
      'POST /v1/jobs/j-old/workflow/apply-template-update': (init) => {
        applied.push(JSON.parse(String(init?.body)));
        return Response.json({ id: 'j-old', title: 'Barista', workflowVersion: 5, template: { templateName: 'Brightwater engineering', version: 2, updateAvailable: false } });
      },
    });

    await screen.findByText('1 job uses an older version.');
    expect(fact('Status')).toHaveTextContent('Published');
    await fireEvent.click(within(header()).getByRole('button', { name: 'Review updates' }));
    const sheet = await screen.findByRole('dialog', { name: 'Review updates' });
    const jobs = within(sheet).getByRole('list', { name: 'Jobs on an older version' });
    expect(within(jobs).getAllByRole('listitem')).toHaveLength(1);
    expect(within(jobs).getByRole('link', { name: 'See what changes for Barista' })).toHaveAttribute('href', '/jobs/j-old/workflow?review=update');

    await fireEvent.click(within(jobs).getByRole('button', { name: 'Update Barista' }));
    const dialog = await screen.findByRole('dialog', { name: 'Apply version 2 of Brightwater engineering to Barista?' });
    await within(dialog).findByText('No differences in the pipeline.');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Apply update' }));

    await waitFor(() => expect(applied).toEqual([{ templateVersionId: 'v-2', expectedWorkflowVersion: 4 }]));
  });

  it('leaves Update out for a role that may not change jobs', async () => {
    pageState.data.user = userAs('Viewer');
    renderPublished('http://dashboard.test/templates/t-1?version=2&tab=pipeline');

    await fireEvent.click(await screen.findByRole('button', { name: 'Review updates' }));
    const sheet = await screen.findByRole('dialog', { name: 'Review updates' });
    expect(within(sheet).getByRole('link', { name: 'See what changes for Barista' })).toBeInTheDocument();
    expect(within(sheet).queryByRole('button', { name: /^Update/ })).toBeNull();
  });
});
