import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, applications } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { url: new URL('http://dashboard.test/candidates') } }));

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('candidates page', () => {
  it('counts the ticked rows in a pill beside the count, as the table has no footer (K1, W3)', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/applications': () => Response.json(applications) })));
    render(WithQueryClient, { props: { component: Page, props: {} } });
    expect(await screen.findByText(`${applications.length} applications`)).toBeInTheDocument();
    expect(screen.queryByText(/selected$/)).toBeNull();

    await fireEvent.click(screen.getByRole('checkbox', { name: `Select ${applications[0]!.candidateName}` }));

    expect(screen.getByText('1 selected')).toHaveAttribute('data-slot', 'badge');
  });
});
