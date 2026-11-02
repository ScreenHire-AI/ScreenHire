import { render, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { apiStub, viewList } from '../test-support/fixtures.js';
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
