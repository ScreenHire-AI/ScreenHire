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
  it('counts the ticked rows in its count line, as the table has no footer (K1)', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/applications': () => Response.json(applications) })));
    render(WithQueryClient, { props: { component: Page, props: {} } });
    const total = `${applications.length} applications`;
    expect(await screen.findByText(total)).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('checkbox', { name: `Select ${applications[0]!.candidateName}` }));

    expect(screen.getByText(`${total}, 1 selected`)).toBeInTheDocument();
  });
});
