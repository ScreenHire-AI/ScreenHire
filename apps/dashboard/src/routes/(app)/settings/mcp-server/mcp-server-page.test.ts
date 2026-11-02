import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/mcp-server') }, navigating: { to: null } }));

afterEach(() => vi.unstubAllGlobals());

describe('MCP server page', () => {
  it('lists every tool, with the two verification tools apart as the ones that need the scope', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({})));
    render(WithQueryClient, { props: { component: Page, props: {} } });

    const tools = await screen.findByRole('list', { name: 'Tools' });
    const verification = screen.getByRole('list', { name: 'With Verification read' });

    expect(within(tools).getAllByRole('listitem')).toHaveLength(10);
    expect(within(tools).getByText('Read the hiring report')).toBeInTheDocument();
    expect(within(tools).getByText('List saved candidates')).toBeInTheDocument();
    expect(within(tools).queryByText(/verification/i)).not.toBeInTheDocument();
    expect(within(verification).getAllByRole('listitem')).toHaveLength(2);
    await expectNoAxeViolations();
  });
});
