import { render, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { apiStub, savedView, viewList } from '../test-support/fixtures.js';
import ViewActionsHarness from '../test-support/view-actions-harness.svelte';
import WithQueryClient from '../test-support/with-query-client.svelte';
import type { ViewActions } from './views.svelte.js';

describe('hiding built-in views', () => {
  it('keeps the first of two quick changes, because the route replaces the whole list', async () => {
    let hidden: string[] = [];
    const sent: string[][] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/views': () => Response.json(viewList([], hidden)),
          'PUT /v1/views/hidden-built-ins': (init) => {
            const body = JSON.parse(String(init?.body)) as { hiddenBuiltIns: string[] };
            sent.push(body.hiddenBuiltIns);
            hidden = body.hiddenBuiltIns;
            return Response.json(viewList([], hidden));
          },
        }),
      ),
    );
    let actions: ViewActions | undefined;
    render(WithQueryClient, { props: { component: ViewActionsHarness, props: { onready: (ready: ViewActions) => (actions = ready) } } });
    await waitFor(() => expect(actions).toBeDefined());

    await Promise.all([actions!.setHidden('overdue', true), actions!.setHidden('new-today', true)]);

    expect(sent).toEqual([['overdue'], ['overdue', 'new-today']]);
  });
});

describe("a person's own sidebar", () => {
  it('adds and removes a shared view for themselves, and notes that they opened the team views, each refetching the list', async () => {
    const offered = savedView({ id: 'offered', name: 'Team queue', shared: true, mine: false, canEdit: false, inSidebar: false });
    const calls: string[] = [];
    let lists = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/views': () => {
            lists += 1;
            return Response.json(viewList([offered]));
          },
          'PUT /v1/views/offered/sidebar': () => {
            calls.push('add');
            return new Response(null, { status: 204 });
          },
          'DELETE /v1/views/offered/sidebar': () => {
            calls.push('remove');
            return new Response(null, { status: 204 });
          },
          'PUT /v1/views/team-views-opened': () => {
            calls.push('opened');
            return new Response(null, { status: 204 });
          },
        }),
      ),
    );
    let actions: ViewActions | undefined;
    render(WithQueryClient, { props: { component: ViewActionsHarness, props: { onready: (ready: ViewActions) => (actions = ready) } } });
    await waitFor(() => expect(actions).toBeDefined());
    await waitFor(() => expect(lists).toBe(1));

    expect(await actions!.addToSidebar(offered)).toBeNull();
    expect(await actions!.removeFromSidebar(offered)).toBeNull();
    expect(await actions!.markTeamViewsOpened()).toBeNull();

    expect(calls).toEqual(['add', 'remove', 'opened']);
    await waitFor(() => expect(lists).toBeGreaterThan(1));
  });

  it('says what failed when a view cannot be added', async () => {
    const offered = savedView({ id: 'offered', name: 'Team queue', shared: true, mine: false, canEdit: false, inSidebar: false });
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/views': () => Response.json(viewList([offered])),
          'PUT /v1/views/offered/sidebar': () => new Response(null, { status: 404 }),
        }),
      ),
    );
    let actions: ViewActions | undefined;
    render(WithQueryClient, { props: { component: ViewActionsHarness, props: { onready: (ready: ViewActions) => (actions = ready) } } });
    await waitFor(() => expect(actions).toBeDefined());

    expect(await actions!.addToSidebar(offered)).toMatch(/Could not add "Team queue" to your sidebar/);
  });
});
