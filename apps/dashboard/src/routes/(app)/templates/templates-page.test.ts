import type { HiringTemplateSummary } from '@screenhire/api-client';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, user } from '../../../test-support/fixtures.js';
import { stubViewport } from '../../../test-support/viewport.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/templates') }, navigating: { to: null } }));

const template = (id: string, name: string, description: string | null): HiringTemplateSummary => ({
  id,
  name,
  description,
  isSystem: false,
  latestPublishedVersion: 1,
  latestPublishedVersionId: `${id}-v1`,
  draftVersion: null,
  createdAt: '2026-10-01T09:00:00Z',
  updatedAt: null,
});

function stubTemplates(templates: HiringTemplateSummary[]) {
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/templates': () => Response.json(templates) })));
}

const renderPage = () => render(WithQueryClient, { props: { component: Page, props: {} } });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('templates page', () => {
  it('filters by name or description, and clears a search that matches nothing', async () => {
    stubTemplates([
      template('t-1', 'Engineering loop', 'Phone screen, then a take-home'),
      template('t-2', 'Retail associate', 'One interview in store'),
    ]);
    renderPage();
    await screen.findAllByText('Retail associate');
    const search = screen.getByRole('searchbox', { name: 'Search templates' });

    await fireEvent.input(search, { target: { value: 'take-home' } });
    expect(screen.getAllByText('Engineering loop').length).toBeGreaterThan(0);
    expect(screen.queryAllByText('Retail associate')).toHaveLength(0);

    await fireEvent.input(search, { target: { value: 'nothing like this' } });
    expect(screen.getByText('No templates match')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Clear search' }));
    expect(screen.getAllByText('Retail associate').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Engineering loop').length).toBeGreaterThan(0);
  });

  it('puts New template in the action bar on a phone', async () => {
    stubViewport(360);
    stubTemplates([template('t-1', 'Engineering loop', null)]);
    renderPage();
    await screen.findAllByText('Engineering loop');

    const buttons = screen.getAllByRole('button', { name: 'New template' });
    expect(buttons).toHaveLength(1);
    expect(buttons[0]!.closest('[data-slot=action-bar]')).not.toBeNull();
  });

  it('offers a new template when there are none', async () => {
    stubTemplates([]);
    renderPage();

    expect(await screen.findByText('No templates yet')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Clear search' })).toBeNull();
    expect(screen.getAllByRole('button', { name: 'New template' }).length).toBeGreaterThan(0);
  });
});
