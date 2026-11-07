import { describe, expect, it } from 'vitest';
import { filterIntegrations, integrationKinds, integrations, type Integration } from './integrations.js';
import { settingsSections } from './settings.js';

const fake = (index: number, kind: string): Integration => ({
  label: `Integration ${index}`,
  href: `/settings/integration-${index}`,
  icon: integrations[0]!.icon,
  description: index === 7 ? 'Syncs interviews with a calendar.' : 'Does something useful.',
  kind,
  permission: 'ManageIntegrations',
});

describe('integrations', () => {
  it('feed the Settings pages, so a new one is one entry (S9)', () => {
    const pages = settingsSections.filter((section) => section.group === 'Integrations');

    expect(pages.map((page) => [page.label, page.href, page.permission])).toEqual(
      integrations.map((integration) => [integration.label, integration.href, integration.permission]),
    );
    // Reaching every job and needing a feature carry over, as the page gate reads them.
    expect(pages.find((page) => page.label === 'Slack')).toMatchObject({ allJobs: true, feature: 'slack' });
    expect(pages.find((page) => page.label === 'MCP server')?.feature).toBeUndefined();
  });

  it('has a one-line description and a kind each, and a way to tell its state only where one exists', () => {
    for (const integration of integrations) {
      expect(integration.description).not.toContain('\n');
      expect(integration.kind).not.toBe('');
    }
    expect(integrations.find((integration) => integration.label === 'MCP server')?.useConnection).toBeUndefined();
  });

  it('groups the kinds in the order they first appear', () => {
    expect(integrationKinds(integrations)).toEqual(['Chat', 'AI assistants', 'Developer']);
  });

  it('narrows by kind and by words of the name or description, and scales to twenty', () => {
    const twenty = Array.from({ length: 20 }, (_, index) => fake(index + 1, index % 2 === 0 ? 'Calendar' : 'Chat'));

    expect(filterIntegrations(twenty, '', null)).toHaveLength(20);
    expect(filterIntegrations(twenty, '', 'Calendar')).toHaveLength(10);
    expect(filterIntegrations(twenty, 'integration 12', null).map((integration) => integration.label)).toEqual(['Integration 12']);
    expect(filterIntegrations(twenty, ' CALENDAR ', null).map((integration) => integration.label)).toEqual(['Integration 7']);
    expect(filterIntegrations(twenty, 'calendar', 'Calendar')).toEqual([twenty[6]]);
    expect(filterIntegrations(twenty, 'calendar', 'Chat')).toEqual([]);
    expect(filterIntegrations(twenty, 'nothing like it', null)).toEqual([]);
  });
});
