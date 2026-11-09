// A hiring template's draft: Save draft always works, even with problems, and Publish waits until the workflow is valid.
import type { CurrentUser, DefinitionError, HiringTemplateDetail, HiringTemplateVersionDetail, HiringWorkflow } from '@screenhire/api-client';
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

function renderPage() {
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/templates/t-1': () => Response.json(detail),
      'GET /v1/templates/t-1/versions/1': () => Response.json(version()),
      'GET /v1/templates/t-1/hiring-team': () => Response.json([]),
      'GET /v1/template-usage': () => Response.json({ hiring: [], scorecards: [], emails: [] }),
      'GET /v1/organization/members': () => Response.json([]),
      'GET /v1/organization/verification-settings': () => Response.json({ defaultAiUse: 'NotAllowed' }),
      'GET /v1/scorecard-templates': () => Response.json([]),
      'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false, variables: [] }),
      'POST /v1/workflows/validate': () => Response.json({ valid: problems.length === 0, errors: problems }),
      'PUT /v1/templates/t-1/versions/1': (init) => Response.json({ ...version(), definition: JSON.parse(String(init?.body)).definition }),
      'POST /v1/templates/t-1/versions/1/publish': () => Response.json({ ...version(), status: 'Published' }),
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
    expect(within(row).getByRole('link', { name: 'Change policy' })).toHaveAttribute('href', '/verification');
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

  it('shows Needs fixes with a Fix button, and keeps Publish version 1 disabled until the workflow is valid', async () => {
    renderPage();

    const publish = await screen.findByRole('button', { name: 'Publish version 1' });
    await screen.findByText('Needs fixes');
    expect(screen.getByRole('button', { name: 'Fix 1 thing' })).toBeInTheDocument();
    expect(publish).toBeDisabled();
  });

  it('enables Publish once the validator finds nothing, and Publish saves the draft first and then publishes it', async () => {
    problems = [];
    const fetchMock = renderPage();
    const publish = await screen.findByRole('button', { name: 'Publish version 1' });
    await waitFor(() => expect(publish).toBeEnabled());
    expect(screen.queryByText('Needs fixes')).toBeNull();

    await addAStage();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish version 1' })).toBeEnabled());
    await fireEvent.click(screen.getByRole('button', { name: 'Publish version 1' }));

    await waitFor(() => expect(calls(fetchMock, 'POST', '/v1/templates/t-1/versions/1/publish')).toHaveLength(1));
    expect(calls(fetchMock, 'PUT', '/v1/templates/t-1/versions/1')).toHaveLength(1);
  });

  it('turns Publish off again the moment an edit is made, until that edit is checked', async () => {
    problems = [];
    renderPage();
    const publish = await screen.findByRole('button', { name: 'Publish version 1' });
    await waitFor(() => expect(publish).toBeEnabled());

    await fireEvent.click(await screen.findByRole('button', { name: 'Add a stage after Applied' }));

    expect(screen.getByRole('button', { name: 'Publish version 1' })).toBeDisabled();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Publish version 1' })).toBeEnabled());
  });
});
