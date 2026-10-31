import GlobeIcon from '@lucide/svelte/icons/globe';
import Building2Icon from '@lucide/svelte/icons/building-2';
import HashIcon from '@lucide/svelte/icons/hash';
import KeyRoundIcon from '@lucide/svelte/icons/key-round';
import PlugIcon from '@lucide/svelte/icons/plug';
import PaletteIcon from '@lucide/svelte/icons/palette';
import ShieldUserIcon from '@lucide/svelte/icons/shield-user';
import ShieldCheckIcon from '@lucide/svelte/icons/shield-check';
import UsersRoundIcon from '@lucide/svelte/icons/users-round';
import WebhookIcon from '@lucide/svelte/icons/webhook';
import type { CurrentUser, JobAccess, OrganizationRole, Permission, RoleSummary } from '@screenhire/api-client';
import { can, seesAllJobs } from '#lib/permissions.js';
import type { LucideIcon } from '#lib/shell.js';

/** The team's history and its Last updated (`/team/history`, `/team/last-update`): invalidate it with every team change. */
export const teamPath = '/v1/organization/team';

/**
 * The Settings menu's pages (design system section 11), each with the permission its changes need, in three groups the
 * menu heads with their names: the organization and its people, how hiring looks and works, and what connects to it.
 * The sidebar's one Settings item opens the first page the person may use, and the command palette lists those. Kept
 * apart from `sections`, which every member sees and which the G chords read. Email templates live on the Templates
 * page (section 21), and their old addresses here redirect there.
 */
export type SettingsGroup = 'Organization' | 'Hiring' | 'Integrations';

export const settingsSections: readonly {
  label: string;
  href: string;
  icon: LucideIcon;
  permission: Permission;
  group: SettingsGroup;
  allJobs?: true;
}[] = [
  // The group is Organization, so its first page is General: the name, where the data lives and when it was created.
  { label: 'General', href: '/settings/organization', icon: Building2Icon, permission: 'EditSettings', group: 'Organization' },
  { label: 'Team', href: '/settings/team', icon: UsersRoundIcon, permission: 'ManageTeam', group: 'Organization' },
  { label: 'Roles', href: '/settings/roles', icon: ShieldUserIcon, permission: 'ManageTeam', group: 'Organization' },
  { label: 'Careers site', href: '/settings/careers-site', icon: GlobeIcon, permission: 'EditSettings', group: 'Hiring' },
  { label: 'Branding', href: '/settings/branding', icon: PaletteIcon, permission: 'EditSettings', group: 'Hiring' },
  { label: 'Verification', href: '/settings/verification', icon: ShieldCheckIcon, permission: 'EditSettings', group: 'Hiring' },
  // A channel post reaches everyone in the channel, whatever jobs they see, so Slack needs all jobs too. Each person links
  // their own account from their menu (SlackAccountSheet), not here.
  { label: 'Slack', href: '/settings/slack', icon: HashIcon, permission: 'ManageIntegrations', group: 'Integrations', allJobs: true },
  // The MCP server signs in with an API key, so it needs what API keys need.
  { label: 'MCP server', href: '/settings/mcp-server', icon: PlugIcon, permission: 'ManageApiKeys', group: 'Integrations', allJobs: true },
  { label: 'Webhooks', href: '/settings/webhooks', icon: WebhookIcon, permission: 'ManageWebhooks', group: 'Integrations', allJobs: true },
  { label: 'API keys', href: '/settings/api-keys', icon: KeyRoundIcon, permission: 'ManageApiKeys', group: 'Integrations', allJobs: true },
];

/**
 * The Settings pages the person's role may use: none means no Settings item at all. API keys, webhooks and Slack reach every
 * job, so they also need all jobs (ADR-0015); the API refuses them otherwise with 403 "Needs all jobs".
 */
export const visibleSettingsSections = (user: CurrentUser | null | undefined) =>
  settingsSections.filter((section) => can(user, section.permission) && (!section.allJobs || seesAllJobs(user)));

/** The Settings page a path belongs to, such as Roles for `/settings/roles/compare`. */
export const settingsSectionOf = (path: string) =>
  settingsSections.find((section) => path === section.href || path.startsWith(`${section.href}/`));

/**
 * A role as one key: a built-in role's name in lower case (`viewer`), or a custom role's id. It is the role page's
 * address (`/settings/roles/viewer`) and the value of every role picker.
 */
export const roleKey = (role: { role: OrganizationRole; customRoleId?: string | null }): string =>
  role.role === 'Custom' ? (role.customRoleId ?? '') : role.role.toLowerCase();

/** The request body that gives the role with `key`. */
export const roleFromKey = (roles: readonly RoleSummary[], key: string): { role: OrganizationRole; customRoleId: string | null } | undefined => {
  const found = roles.find((role) => roleKey(role) === key);
  return found && { role: found.role, customRoleId: found.customRoleId };
};

// The API's TeamPolicy (src/ScreenHire.Domain/Organizations/TeamPolicy.cs), so screens offer only what it allows: a person
// whose role has Manage the team may give, take away or edit a role only when it holds nothing their own role does not.
// For the built-in roles that is Owners managing everyone and Admins everyone but Owners. The API stays the judge: it
// refuses anything else with 403 "Owners only" or "More access than your role", and the last Owner with 409.

/** Whether someone with `actor` may invite someone as, move someone into or out of, or remove someone with, a role with `role`. */
export const mayAssign = (actor: readonly Permission[], role: readonly Permission[]): boolean =>
  actor.includes('ManageTeam') && role.every((permission) => actor.includes(permission));

/** The roles someone with `actor` may give, in the catalogue's order. */
export const assignableRoles = (actor: readonly Permission[], roles: readonly RoleSummary[]): RoleSummary[] =>
  roles.filter((role) => mayAssign(actor, role.permissions));

/** Whether a member with `role` and `access` sees every job: Owners and Admins always do (JobAccessRules on the API). */
export const seesAllJobsWith = (role: OrganizationRole, access: JobAccess): boolean =>
  role === 'Owner' || role === 'Admin' || access === 'All';

/**
 * The API's job-access subset rule (TeamPolicy.MayGiveJobAccess): someone who sees only their own jobs may not leave
 * anyone seeing every job, through All jobs or through the Owner or Admin role.
 */
export const mayGiveJobAccess = (actorSeesAllJobs: boolean, role: OrganizationRole, access: JobAccess): boolean =>
  actorSeesAllJobs || !seesAllJobsWith(role, access);
