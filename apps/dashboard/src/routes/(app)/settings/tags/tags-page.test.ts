import type { TagList } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { expectCountInPanelNotBand } from '../../../../test-support/list-template.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/tags') }, navigating: { to: null } }));

const engineering = { templateId: '01a157d4-0000-7000-8000-0000000000e1', name: 'Engineering', version: 2 };
const catalogue: TagList = {
  tags: [
    { name: 'Senior', applications: 4, templates: [engineering] },
    { name: 'Sr', applications: 2, templates: [] },
    { name: 'Hot', applications: 0, templates: [engineering] },
  ],
};

function renderPage(routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: Record<string, unknown[]> = {};
  const record = (key: string, init: RequestInit | undefined) => (bodies[key] ??= []).push(init?.body ? JSON.parse(String(init.body)) : null);
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/tags': () => Response.json(catalogue),
        'POST /v1/tags/rename': (init) => {
          record('rename', init);
          return Response.json({ jobs: 1, drafts: 0, views: 0, stillNamedBy: [engineering] });
        },
        'POST /v1/tags/merge': (init) => {
          record('merge', init);
          return Response.json({ jobs: 0, drafts: 0, views: 0, stillNamedBy: [] });
        },
        'DELETE /v1/tags?tag=Sr': (init) => {
          record('delete', init);
          return Response.json({ jobs: 0, drafts: 0, views: 0, stillNamedBy: [] });
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return bodies;
}

// bits-ui opens a menu on pointerdown. Rows render in a phone list and a table (no CSS in jsdom), so take the first.
async function choose(tag: string, item: string) {
  const [trigger] = await screen.findAllByRole('button', { name: `Actions for ${tag}` });
  await fireEvent.pointerDown(trigger!, { button: 0, pointerType: 'mouse' });
  await fireEvent.click(within(await screen.findByRole('menu')).getByRole('menuitem', { name: item }));
}

afterEach(() => vi.unstubAllGlobals());

describe('tags page', () => {
  it('lists each tag with its candidates and the published templates that name it, most used first', async () => {
    renderPage();
    const table = (await screen.findAllByRole('table'))[0]!;
    const rows = within(table).getAllByRole('row').slice(1);

    expect(rows.map((row) => within(row).getAllByRole('cell').slice(0, 3).map((cell) => cell.textContent?.trim()))).toEqual([
      ['Senior', '4', 'Engineering (version 2)'],
      ['Sr', '2', ''],
      ['Hot', '0', 'Engineering (version 2)'],
    ]);
    expectCountInPanelNotBand('Tags', 3);
    await expectNoAxeViolations();
  });

  it('offers a search past ten tags, matching the name ignoring case', async () => {
    const many = Array.from({ length: 11 }, (_, index) => ({ name: `Tag ${index + 1}`, applications: 11 - index, templates: [] }));
    renderPage({ 'GET /v1/tags': () => Response.json({ tags: [...many, { name: 'Night shift', applications: 0, templates: [] }] }) });

    await fireEvent.input(await screen.findByRole('searchbox', { name: 'Search tags' }), { target: { value: 'NIGHT' } });

    const table = screen.getAllByRole('table')[0]!;
    expect(within(table).getAllByRole('row').slice(1).map((row) => within(row).getAllByRole('cell')[0]!.textContent?.trim())).toEqual(['Night shift']);
    expectCountInPanelNotBand('Tags', 12);
  });

  it('renames a tag and says which published template still names the old one', async () => {
    const bodies = renderPage();
    await choose('Sr', 'Rename');

    const dialog = await screen.findByRole('dialog', { name: 'Rename tag' });
    await fireEvent.input(within(dialog).getByLabelText('Name'), { target: { value: ' Staff ' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Rename tag' }));

    await waitFor(() => expect(bodies.rename).toEqual([{ tag: 'Sr', newName: 'Staff' }]));
    const notice = await screen.findByText(/Engineering still names "Sr" in its published version/);
    expect(notice.closest('[data-slot="notice"]')).toHaveTextContent('Publish a new version to use the new name.');
    expect(screen.getByRole('link', { name: 'Open Engineering' })).toHaveAttribute('href', `/templates/${engineering.templateId}`);
    expect(screen.getByText('Renamed "Sr" to "Staff".')).toHaveClass('sr-only');
  });

  it('shows why a rename was refused under the field, as Merge is the way to combine two tags', async () => {
    renderPage({ 'POST /v1/tags/rename': () => Response.json({ detail: '"Senior" is already a tag. Merge into it to combine the two.' }, { status: 409 }) });
    await choose('Sr', 'Rename');

    const dialog = await screen.findByRole('dialog', { name: 'Rename tag' });
    await fireEvent.input(within(dialog).getByLabelText('Name'), { target: { value: 'senior' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Rename tag' }));

    expect(await within(dialog).findByText('"Senior" is already a tag. Merge into it to combine the two.')).toBeInTheDocument();
  });

  it('merges a tag into one of the others', async () => {
    const bodies = renderPage();
    await choose('Sr', 'Merge into another tag');

    const dialog = await screen.findByRole('dialog', { name: 'Merge "Sr" into another tag' });
    const into = within(dialog).getByLabelText('Merge into');
    expect(within(into).getAllByRole('option').map((option) => option.textContent)).toEqual(['Choose a tag', 'Senior', 'Hot']);
    await fireEvent.change(into, { target: { value: 'Senior' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Merge tags' }));

    await waitFor(() => expect(bodies.merge).toEqual([{ tag: 'Sr', into: 'Senior' }]));
  });

  it('deletes a tag after asking, and offers no Delete for a tag no candidate has', async () => {
    const bodies = renderPage();
    const [hot] = await screen.findAllByRole('button', { name: 'Actions for Hot' });
    await fireEvent.pointerDown(hot!, { button: 0, pointerType: 'mouse' });
    expect(within(await screen.findByRole('menu')).queryByRole('menuitem', { name: 'Delete tag' })).toBeNull();
    await fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

    await choose('Sr', 'Delete tag');
    const dialog = await screen.findByRole('dialog', { name: 'Delete the tag "Sr"?' });
    expect(dialog).toHaveTextContent('It comes off 2 candidates.');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete tag' }));

    await waitFor(() => expect(bodies.delete).toHaveLength(1));
  });
});
