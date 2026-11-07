import HashIcon from '~icons/lucide/hash';
import KeyRoundIcon from '~icons/lucide/key-round';
import PlugIcon from '~icons/lucide/plug';
import WebhookIcon from '~icons/lucide/webhook';
import { createGetSlack, createListApiKeys, createListWebhookEndpoints } from '@screenhire/api-client/svelte-query';
import type { CurrentUser, Permission } from '@screenhire/api-client';
import { can, seesAllJobs } from '#lib/permissions.js';
import type { SettingsFeature } from '#lib/settings.js';
import type { LucideIcon } from '#lib/shell.js';

/** Whether an integration is set up. An integration with no way to tell has no `useConnection` and shows no state. */
export type Connection = 'connected' | 'notConnected';

export interface Integration {
  /** The name on its row, its Settings menu entry and its breadcrumb. */
  label: string;
  /** Its existing page. Pages below it (a webhook endpoint) are records of that page. */
  href: string;
  icon: LucideIcon;
  /** One line on what it does. */
  description: string;
  /** What the directory's filter groups it under. */
  kind: string;
  /** The permission its page needs (the API refuses anyone else). */
  permission: Permission;
  /** Reaches every job, so it also needs all jobs (ADR-0015). */
  allJobs?: true;
  /** The page exists only where this feature is set up (design system A3). */
  feature?: SettingsFeature;
  /**
   * Called while the row's component starts, so it can create the query that tells whether the integration is set up. It
   * returns a getter for the answer, missing while that loads. The query is off for anyone the API would refuse.
   */
  useConnection?: (user: CurrentUser | null | undefined) => () => Connection | undefined;
}

const allowed = (user: CurrentUser | null | undefined, permission: Permission) => can(user, permission) && seesAllJobs(user);

/**
 * Every integration, in the order the directory lists them. Adding one is one more entry here: its Settings page entry,
 * its permission gate, the sidebar's Integrations item, Ctrl K and the directory (`/settings/integrations`, design system
 * S9) all read this list. Personal connections (a member's calendar, their Slack account) are not integrations of the
 * organization and stay in the account menu.
 */
export const integrations: readonly Integration[] = [
  {
    label: 'Slack',
    href: '/settings/slack',
    icon: HashIcon,
    description: 'Post hiring updates to a channel and ask for scorecards in direct messages.',
    kind: 'Chat',
    // A channel post reaches everyone in the channel, whatever jobs they see, so Slack needs all jobs too. Each person links
    // their own account from their menu (SlackAccountSheet), not here.
    permission: 'ManageIntegrations',
    allJobs: true,
    feature: 'slack',
    useConnection: () => {
      // Anyone may read it (the shell asks the same question), so there is no permission to wait for.
      const slack = createGetSlack(() => ({ query: { staleTime: 60_000 } }));
      return () => {
        const status = slack.data?.data;
        return status ? (status.workspace && status.settings ? 'connected' : 'notConnected') : undefined;
      };
    },
  },
  // No answer tells whether an assistant uses it, so its row shows no state (design system W6).
  {
    label: 'MCP server',
    href: '/settings/mcp-server',
    icon: PlugIcon,
    description: 'Let an AI assistant read your hiring data with an API key.',
    kind: 'AI assistants',
    // The MCP server signs in with an API key, so it needs what API keys need.
    permission: 'ManageApiKeys',
    allJobs: true,
  },
  {
    label: 'Webhooks',
    href: '/settings/webhooks',
    icon: WebhookIcon,
    description: 'Send hiring events to your own systems as they happen.',
    kind: 'Developer',
    permission: 'ManageWebhooks',
    allJobs: true,
    useConnection: (user) => {
      const endpoints = createListWebhookEndpoints(() => ({ query: { enabled: allowed(user, 'ManageWebhooks'), staleTime: 60_000 } }));
      return () => {
        const list = endpoints.data?.data;
        // An endpoint that is turned off, or turned itself off after failing, delivers nothing.
        return list ? (list.some((endpoint) => endpoint.enabled) ? 'connected' : 'notConnected') : undefined;
      };
    },
  },
  {
    label: 'API keys',
    href: '/settings/api-keys',
    icon: KeyRoundIcon,
    description: 'Let your own software use the hiring API.',
    kind: 'Developer',
    permission: 'ManageApiKeys',
    allJobs: true,
    useConnection: (user) => {
      const keys = createListApiKeys(() => ({ query: { enabled: allowed(user, 'ManageApiKeys'), staleTime: 60_000 } }));
      return () => {
        const list = keys.data?.data;
        return list ? (list.some((key) => key.revokedAt === null) ? 'connected' : 'notConnected') : undefined;
      };
    },
  },
];

/** The kinds the directory filters by, in the order they first appear. */
export const integrationKinds = (list: readonly Integration[]) => [...new Set(list.map((integration) => integration.kind))];

/** The directory narrowed by a search (name or description) and a kind; `null` kind is all of them. */
export const filterIntegrations = (list: readonly Integration[], search: string, kind: string | null) => {
  const needle = search.trim().toLowerCase();
  return list.filter(
    (integration) =>
      (kind === null || integration.kind === kind) &&
      (needle === '' || `${integration.label} ${integration.description}`.toLowerCase().includes(needle)),
  );
};

/** The directory shows a search box past this many integrations (design system S9). */
export const searchThreshold = 10;
