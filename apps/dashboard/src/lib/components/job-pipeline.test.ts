import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { preferences } from '#lib/preferences.svelte.js';
import { applications, stages } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import JobPipeline from './job-pipeline.svelte';

vi.mock('$app/navigation', () => ({ goto: vi.fn(async () => {}) }));

function renderPipeline() {
  return render(WithQueryClient, {
    props: {
      component: JobPipeline,
      props: { jobTitle: 'Senior .NET Engineer', stages, applications, pending: false, error: null, onretry: vi.fn() },
    },
  });
}

const panelHeader = () => screen.getByRole('region', { name: 'Pipeline' }).querySelector('header')!;
const names = () =>
  within(screen.getByRole('table', { name: 'Candidates for Senior .NET Engineer' }))
    .getAllByRole('link')
    .map((link) => link.textContent)
    .filter((text) => applications.some((application) => application.candidateName === text));

afterEach(() => {
  preferences.setShortcutsEnabled(true);
});

describe('job pipeline', () => {
  it('holds search, the stage and integrity filters and the view switch in its panel header (section 7)', () => {
    renderPipeline();
    const header = panelHeader();

    expect(within(header).getByRole('searchbox', { name: 'Search candidates' })).toBeInTheDocument();
    expect(within(header).getByRole('button', { name: /^Stage: All/ })).toBeInTheDocument();
    expect(within(header).getByRole('button', { name: /^Integrity: all/ })).toBeInTheDocument();
    expect(within(within(header).getByRole('group', { name: 'View' })).getByRole('button', { name: 'Table' })).toHaveAttribute('aria-pressed', 'true');
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

  it('switches to the board with V or the view switch; the board keeps its keyboard and menu moves', async () => {
    renderPipeline();
    const link = screen.getByRole('link', { name: 'Casey Morgan' });

    await fireEvent.keyDown(link, { key: 'v' });

    expect(screen.getByRole('list', { name: 'Applied stage' })).toBeInTheDocument();
    expect(within(panelHeader()).queryByRole('button', { name: /^Stage:/ })).toBeNull();
    expect(screen.getByRole('button', { name: 'Move Casey Morgan to another stage' })).toBeInTheDocument();

    await fireEvent.click(within(panelHeader()).getByRole('button', { name: 'Table' }));
    expect(screen.getByRole('table')).toBeInTheDocument();
  });
});
