import { describe, expect, it } from 'vitest';
import { integrations } from './integrations.js';
import { settingsSections } from './settings.js';

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

  it('carries the words that find each one in Settings search, and a one-line description only where the name needs it', () => {
    for (const integration of integrations) {
      expect(integration.keywords.length).toBeGreaterThan(0);
      expect(integration.description ?? '').not.toContain('\n');
    }
    expect(integrations.find((integration) => integration.label === 'Slack')?.description).toBeUndefined();
    expect(integrations.find((integration) => integration.label === 'Webhooks')?.description).toBeUndefined();
    expect(integrations.find((integration) => integration.label === 'MCP server')?.description).toBeDefined();
  });
});
