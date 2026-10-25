import type { WorkflowStage } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { preferences } from '#lib/preferences.svelte.js';
import type { IntegrityFilter } from '#lib/integrity.js';
import { apiStub, applications, stages, user } from '../../test-support/fixtures.js';
import JobPipelineHarness from '../../test-support/job-pipeline-harness.svelte';
import { stubViewport } from '../../test-support/viewport.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';

vi.mock('$app/navigation', () => ({ goto: vi.fn(async () => {}) }));
// Signed out unless a test signs in: the bulk Email button needs a hiring role and a deployment that sends email.
const appPage = vi.hoisted(() => ({ data: {} as Record<string, unknown> }));
vi.mock('$app/state', () => ({ page: appPage }));

function canSendEmail() {
  appPage.data = { user };
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/email-templates': () => Response.json({ sendingEnabled: true, templates: [], variables: [] }) })));
}

function renderPipeline(overrides: { stages?: WorkflowStage[]; integrityFilter?: IntegrityFilter } = {}) {
  return render(WithQueryClient, { props: { component: JobPipelineHarness, props: { stages, applications, ...overrides } } });
}

const panel = () => screen.getByRole('region', { name: 'Pipeline' });
const panelHeader = () => panel().querySelector('header')!;
const filters = () => panel().querySelector<HTMLElement>('[data-filters]')!;
const selectionBar = () => screen.getByRole('region', { name: 'Selected candidates' });
const names = () =>
  within(screen.getByRole('table', { name: 'Candidates for Senior .NET Engineer' }))
    .getAllByRole('link')
    .map((link) => link.textContent)
    .filter((text) => applications.some((application) => application.candidateName === text));

afterEach(() => {
  appPage.data = {};
  preferences.setShortcutsEnabled(true);
  vi.unstubAllGlobals();
});

describe('job pipeline', () => {
  it('holds the search and the view switch in its header, and the Stage filter and authenticity chips on a row under it', () => {
    renderPipeline();
    const header = panelHeader();

    expect(within(header).getByRole('heading', { level: 2, name: 'Pipeline' })).toBeInTheDocument();
    // The job's Candidates tab says how many; the panel does not say it again (W4).
    expect(header.querySelector('[data-slot="badge"]')).toBeNull();
    expect(within(header).getByRole('searchbox', { name: 'Search candidates' })).toBeInTheDocument();
    expect(within(within(header).getByRole('group', { name: 'View' })).getByRole('button', { name: 'Table' })).toHaveAttribute('aria-pressed', 'true');
    expect(within(header).queryByRole('button', { name: /^Integrity/ })).toBeNull();

    expect(within(filters()).getByRole('button', { name: 'Stage: All' })).toBeInTheDocument();
    // The fixtures have no assessment yet, so every candidate is waiting for checks.
    expect(within(within(filters()).getByRole('group', { name: 'Authenticity' })).getByRole('button', { name: `Checks pending ${applications.length}` })).toBeInTheDocument();
  });

  it('gives the search its own row under the header on a phone, where the header has no room for it (F5)', () => {
    stubViewport(360);
    renderPipeline();

    const search = screen.getByRole('searchbox', { name: 'Search candidates' });
    expect(panelHeader()).not.toContainElement(search);
    expect(panel()).toContainElement(search);
    expect(within(panelHeader()).getByRole('group', { name: 'View' })).toBeInTheDocument();
  });

  it('filters by stage from the Stage menu, each stage with its count (T4)', async () => {
    renderPipeline();

    await fireEvent.pointerDown(within(filters()).getByRole('button', { name: 'Stage: All' }), { button: 0, pointerType: 'mouse' });
    const items = (await screen.findAllByRole('menuitemradio')).map((item) => item.textContent?.replace(/\s+/g, ' ').trim());
    expect(items).toEqual([
      `All ${applications.length}`,
      ...stages.map((stage) => `${stage.name} ${applications.filter((application) => application.stage.id === stage.id).length}`),
    ]);
    await fireEvent.click(screen.getByRole('menuitemradio', { name: /^Screening/ }));

    expect(within(filters()).getByRole('button', { name: 'Stage: Screening' })).toBeInTheDocument();
    expect(names()).toEqual(['Jordan Lee']);
  });

  it('filters by an authenticity chip, and a new filter starts a new selection (ADR-0004)', async () => {
    renderPipeline();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));
    expect(selectionBar()).toHaveTextContent('1 selected');

    await fireEvent.click(within(filters()).getByRole('button', { name: /^Checks pending/ }));

    expect(within(filters()).getByRole('button', { name: /^Checks pending/ })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByRole('region', { name: 'Selected candidates' })).toBeNull();
  });

  it('narrows the table by name, and moves only the ticked rows still shown', async () => {
    renderPipeline();
    for (const name of ['Casey Morgan', 'Jordan Lee']) {
      await fireEvent.click(screen.getByRole('checkbox', { name: `Select ${name}` }));
    }
    expect(selectionBar()).toHaveTextContent('2 selected');
    expect(within(selectionBar()).getByRole('button', { name: 'Move 2 selected…' })).toBeInTheDocument();

    await fireEvent.input(within(panelHeader()).getByRole('searchbox', { name: 'Search candidates' }), { target: { value: 'casey' } });

    expect(names()).toEqual(['Casey Morgan']);
    expect(within(selectionBar()).getByRole('button', { name: 'Move 1 selected…' })).toBeInTheDocument();

    await fireEvent.click(within(selectionBar()).getByRole('button', { name: 'Clear selection' }));
    expect(screen.queryByRole('region', { name: 'Selected candidates' })).toBeNull();
  });

  it('says why the table is empty and offers to clear the filters (E1)', async () => {
    renderPipeline();

    await fireEvent.input(within(panelHeader()).getByRole('searchbox', { name: 'Search candidates' }), { target: { value: 'nobody' } });

    expect(screen.getByText('No candidates match these filters.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(within(panelHeader()).getByRole('searchbox', { name: 'Search candidates' })).toHaveValue('');
    // Every row is back, in the job table's order (those needing attention first).
    expect(names().toSorted()).toEqual(applications.map((application) => application.candidateName).toSorted());
  });

  it('offers no stage that ends a candidacy to a bulk move while an integrity filter is set (ADR-0004)', async () => {
    const withEnds: WorkflowStage[] = [
      ...stages,
      { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
      { id: 'withdrawn', name: 'Withdrawn', kind: 'Withdrawn' },
    ];
    // The fixtures have no assessment yet, so the Pending filter shows every row.
    renderPipeline({ stages: withEnds, integrityFilter: { authenticity: 'Pending', aiAssistance: null } });
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));

    await fireEvent.pointerDown(within(selectionBar()).getByRole('button', { name: 'Move 1 selected…' }), { button: 0, pointerType: 'mouse' });

    const items = (await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim());
    expect(items).toEqual(stages.map((stage) => stage.name));
  });

  it('offers Email for the ticked rows, and none while an integrity filter picked them (ADR-0004)', async () => {
    canSendEmail();
    const { unmount } = renderPipeline();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));
    expect(await within(selectionBar()).findByRole('button', { name: 'Email 1 selected' })).toBeInTheDocument();
    unmount();

    // The fixtures have no assessment yet, so the Pending filter shows every row.
    renderPipeline({ integrityFilter: { authenticity: 'Pending', aiAssistance: null } });
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));
    expect(within(selectionBar()).getByRole('button', { name: 'Move 1 selected…' })).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(within(selectionBar()).queryByRole('button', { name: /^Email/ })).toBeNull();
  });

  it('keeps the board when its empty state clears a search', async () => {
    renderPipeline();
    await fireEvent.click(within(panelHeader()).getByRole('button', { name: 'Board' }));
    await fireEvent.input(within(panelHeader()).getByRole('searchbox', { name: 'Search candidates' }), { target: { value: 'nobody' } });

    expect(screen.getByText('No candidates match these filters.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));

    expect(screen.getByRole('list', { name: 'Applied stage' })).toBeInTheDocument();
    expect(within(panelHeader()).getByRole('button', { name: 'Board' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('offers the Stage, Authenticity and Sort chips in one row above the list on a phone (L2, section 8)', () => {
    stubViewport(390);
    renderPipeline();

    expect(screen.queryByRole('table')).toBeNull();
    const toolbar = screen.getByRole('button', { name: 'Sort' }).parentElement!;
    expect(within(toolbar).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Stage: All', 'Authenticity: All', 'Sort']);
  });

  it('keeps every move on a filtered board to one card: no ticks and no selection bar (ADR-0004)', async () => {
    renderPipeline({ integrityFilter: { authenticity: 'Pending', aiAssistance: null } });
    await fireEvent.click(within(panelHeader()).getByRole('button', { name: 'Board' }));

    expect(screen.getByRole('list', { name: 'Applied stage' })).toBeInTheDocument();
    expect(within(panel()).queryAllByRole('checkbox')).toHaveLength(0);
    expect(screen.queryByRole('region', { name: 'Selected candidates' })).toBeNull();
    // Each card moves on its own, from its own menu: a person's decision about that candidate.
    expect(screen.getByRole('button', { name: 'Move Casey Morgan to another stage' })).toBeInTheDocument();
  });

  it('filters the board by an authenticity level, with no Stage filter: its columns are the stages', async () => {
    renderPipeline();
    await fireEvent.click(within(panelHeader()).getByRole('button', { name: 'Board' }));

    expect(within(filters()).queryByRole('button', { name: /^Stage/ })).toBeNull();
    await fireEvent.click(within(filters()).getByRole('button', { name: /^Review suggested/ }));

    expect(screen.getByText('No candidates match these filters.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(screen.getByRole('list', { name: 'Applied stage' })).toBeInTheDocument();
  });

  it('switches to the board with V or the view switch; the board keeps its keyboard and menu moves', async () => {
    renderPipeline();
    const link = screen.getByRole('link', { name: 'Casey Morgan' });

    await fireEvent.keyDown(link, { key: 'v' });

    expect(screen.getByRole('list', { name: 'Applied stage' })).toBeInTheDocument();
    expect(within(panel()).queryByRole('button', { name: /^Stage:/ })).toBeNull();
    expect(screen.getByRole('button', { name: 'Move Casey Morgan to another stage' })).toBeInTheDocument();

    await fireEvent.click(within(panelHeader()).getByRole('button', { name: 'Table' }));
    expect(screen.getByRole('table')).toBeInTheDocument();
  });
});
