import type { Permission } from '@screenhire/api-client';
import type { SettingsFeature } from '#lib/settings.js';

export interface Integration {
  /** The name on the Settings home, its Settings menu entry and its breadcrumb. */
  label: string;
  /** Its existing page. Pages below it (a webhook endpoint) are records of that page. */
  href: string;
  /** One line on what it does, only where the name does not explain itself (design system W9). */
  description?: string;
  /** Everyday words that find it in Settings search and Ctrl K. */
  keywords: readonly string[];
  /** The permission its page needs (the API refuses anyone else). */
  permission: Permission;
  /** Reaches every job, so it also needs all jobs (ADR-0015). */
  allJobs?: true;
  /** The page exists only where this feature is set up (design system A3). */
  feature?: SettingsFeature;
}

/**
 * Every integration, in the order the Settings home lists them. Adding one is one more entry here: its Settings page
 * entry, its permission gate, its Settings menu entry, Ctrl K and the Settings home (`/settings`, design system S9) all
 * read this list. Personal connections (a member's calendar, their Slack account) are not integrations of the
 * organization and stay in the account menu.
 */
export const integrations: readonly Integration[] = [
  {
    label: 'Slack',
    href: '/settings/slack',
    keywords: ['chat', 'channel', 'notifications', 'messages', 'direct messages', 'scorecard'],
    // A channel post reaches everyone in the channel, whatever jobs they see, so Slack needs all jobs too. Each person links
    // their own account from their menu (SlackAccountSheet), not here.
    permission: 'ManageIntegrations',
    allJobs: true,
    feature: 'slack',
  },
  {
    label: 'MCP server',
    href: '/settings/mcp-server',
    description: 'Let an AI assistant work with your jobs',
    keywords: ['ai', 'assistant', 'claude', 'chatgpt', 'model context protocol', 'mcp'],
    // The MCP server signs in with an API key, so it needs what API keys need.
    permission: 'ManageApiKeys',
    allJobs: true,
  },
  {
    label: 'Webhooks',
    href: '/settings/webhooks',
    keywords: ['events', 'callback', 'http', 'endpoint', 'developer', 'notify'],
    permission: 'ManageWebhooks',
    allJobs: true,
  },
  {
    label: 'API keys',
    href: '/settings/api-keys',
    description: 'For developers building on ScreenHire',
    keywords: ['token', 'developer', 'rest', 'access key', 'secret', 'api'],
    permission: 'ManageApiKeys',
    allJobs: true,
  },
];
