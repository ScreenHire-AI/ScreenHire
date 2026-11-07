import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/integrations') }, navigating: { to: null } }));

// Twenty integrations, as the directory must hold (design system S9): the four real ones plus sixteen more entries, and
// nothing else changes. The extra entries are added to the list the Settings pages and the directory both read.
vi.mock(import('#lib/integrations.js'), async (importOriginal) => {
  const actual = await importOriginal();
  const extra = Array.from({ length: 16 }, (_, index) => ({
    ...actual.integrations[0]!,
    label: `Extra ${index + 1}`,
    href: `/settings/extra-${index + 1}`,
    description: index === 11 ? 'Syncs interviews with a calendar.' : 'Does something useful.',
    kind: index % 2 === 0 ? 'Calendar' : 'Chat',
    permission: 'ManageIntegrations' as const,
    feature: undefined,
    useConnection: undefined,
  }));
  return { ...actual, integrations: [...actual.integrations, ...extra] };
});

afterEach(() => vi.unstubAllGlobals());

describe('integrations directory with twenty integrations', () => {
  it('offers search past ten, and the kinds, with no other change', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ 'GET /v1/organization/api-keys': () => Response.json([]), 'GET /v1/webhooks': () => Response.json([]) })));
    render(WithQueryClient, { props: { component: Page, props: {} } });
    const list = () => within(screen.getByRole('list', { name: 'Integrations' })).getAllByRole('listitem');

    // Slack is off until the shell hears that it is on, so nineteen are listed.
    expect(list()).toHaveLength(19);
    const search = screen.getByRole('searchbox', { name: 'Search integrations' });

    await fireEvent.input(search, { target: { value: 'calendar' } });

    expect(list().map((row) => within(row).getByRole('link').textContent?.trim())).toEqual(['Extra 12']);

    await fireEvent.input(search, { target: { value: 'zzz' } });

    expect(screen.getByText('No integrations match')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('button', { name: 'Show all' }));
    expect(list()).toHaveLength(19);
  });
});
