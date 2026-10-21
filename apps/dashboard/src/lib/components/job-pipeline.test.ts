import type { WorkflowStage } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { preferences } from '#lib/preferences.svelte.js';
import type { IntegrityFilter } from '#lib/integrity.js';
import { applications, stages } from '../../test-support/fixtures.js';
import JobPipelineHarness from '../../test-support/job-pipeline-harness.svelte';
import { stubViewport } from '../../test-support/viewport.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';

vi.mock('$app/navigation', () => ({ goto: vi.fn(async () => {}) }));

function renderPipeline(overrides: { stages?: WorkflowStage[]; integrityFilter?: IntegrityFilter } = {}) {
  return render(WithQueryClient, { props: { component: JobPipelineHarness, props: { stages, applications, ...overrides } } });
}

const panel = () => screen.getByRole('region', { name: 'Pipeline' });
const panelHeader = () => panel().querySelector('header')!;
const names = () =>
  within(screen.getByRole('table', { name: 'Candidates for Senior .NET Engineer' }))
    .getAllByRole('link')
    .map((link) => link.textContent)
    .filter((text) => applications.some((application) => application.candidateName === text));

afterEach(() => {
  preferences.setShortcutsEnabled(true);
  vi.unstubAllGlobals();
});

describe('job pipeline', () => {
  it('titles the panel with its count, and holds search, the integrity filter and the view switch in its header (section 7)', () => {
    renderPipeline();
    const header = panelHeader();

    expect(within(header).getByRole('heading', { level: 2, name: 'Pipeline' })).toBeInTheDocument();
    expect(header.querySelector('[data-slot="badge"]')).toHaveTextContent(String(applications.length));
    expect(within(header).getByRole('searchbox', { name: 'Search candidates' })).toBeInTheDocument();
    expect(within(header).getByRole('button', { name: /^Integrity: all/ })).toBeInTheDocument();
    expect(within(within(header).getByRole('group', { name: 'View' })).getByRole('button', { name: 'Table' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('gives the search its own row under the header on a phone, where the header has no room for it (F5)', () => {
    stubViewport(360);
    renderPipeline();

    const search = screen.getByRole('searchbox', { name: 'Search candidates' });
    expect(panelHeader()).not.toContainElement(search);
    expect(panel()).toContainElement(search);
    expect(within(panelHeader()).getByRole('group', { name: 'View' })).toBeInTheDocument();
  });

  it('filters by stage with a group of toggles on a row under the header, each with its count (T4)', async () => {
    renderPipeline();
    const group = within(panel()).getByRole('group', { name: 'Stage' });

    expect(within(group).getByRole('button', { name: 'All' })).toHaveAttribute('aria-pressed', 'true');
    await fireEvent.click(within(group).getByRole('button', { name: /^Screening/ }));

    expect(within(group).getByRole('button', { name: /^Screening/ })).toHaveAttribute('aria-pressed', 'true');
    expect(names()).toEqual(['Jordan Lee']);
  });

  it('narrows the table by name, and moves only the ticked rows still shown', async () => {
    renderPipeline();
    for (const name of ['Casey Morgan', 'Jordan Lee']) {
      await fireEvent.click(screen.getByRole('checkbox', { name: `Select ${name}` }));
    }
    expect(within(panelHeader()).getByRole('button', { name: 'Move 2 selected…' })).toBeInTheDocument();

    await fireEvent.input(within(panelHeader()).getByRole('searchbox', { name: 'Search candidates' }), { target: { value: 'casey' } });

    expect(names()).toEqual(['Casey Morgan']);
    expect(within(panelHeader()).getByRole('button', { name: 'Move 1 selected…' })).toBeInTheDocument();
  });

  it('says why the table is empty and offers to clear the filters (E1)', async () => {
    renderPipeline();

    await fireEvent.input(within(panelHeader()).getByRole('searchbox', { name: 'Search candidates' }), { target: { value: 'nobody' } });

    expect(screen.getByText('No candidates match these filters.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(within(panelHeader()).getByRole('searchbox', { name: 'Search candidates' })).toHaveValue('');
    expect(names()).toEqual(applications.map((application) => application.candidateName));
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

    await fireEvent.pointerDown(within(panelHeader()).getByRole('button', { name: 'Move 1 selected…' }), { button: 0, pointerType: 'mouse' });

    const items = (await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim());
    expect(items).toEqual(stages.map((stage) => stage.name));
  });

  it('keeps the board when its empty state clears a search', async () => {
    renderPipeline();
    await fireEvent.click(within(panelHeader()).getByRole('button', { name: 'Board' }));
    await fireEvent.input(within(panelHeader()).getByRole('searchbox', { name: 'Search candidates' }), { target: { value: 'nobody' } });

    expect(screen.getByText('No candidates match this search.')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));

    expect(screen.getByRole('list', { name: 'Applied stage' })).toBeInTheDocument();
    expect(within(panelHeader()).getByRole('button', { name: 'Board' })).toHaveAttribute('aria-pressed', 'true');
  });

  it("hands the cursor's row to the page, which shows it in its side column", async () => {
    renderPipeline();
    expect(screen.getByLabelText('Selected candidate')).toHaveTextContent('');

    const link = screen.getByRole('link', { name: 'Jordan Lee' });
    link.focus();
    await fireEvent.focusIn(link);

    expect(screen.getByLabelText('Selected candidate')).toHaveTextContent('Jordan Lee');
  });

  it('switches to the board with V or the view switch; the board keeps its keyboard and menu moves', async () => {
    renderPipeline();
    const link = screen.getByRole('link', { name: 'Casey Morgan' });

    await fireEvent.keyDown(link, { key: 'v' });

    expect(screen.getByRole('list', { name: 'Applied stage' })).toBeInTheDocument();
    expect(within(panel()).queryByRole('group', { name: 'Stage' })).toBeNull();
    expect(screen.getByRole('button', { name: 'Move Casey Morgan to another stage' })).toBeInTheDocument();

    await fireEvent.click(within(panelHeader()).getByRole('button', { name: 'Table' }));
    expect(screen.getByRole('table')).toBeInTheDocument();
  });
});
