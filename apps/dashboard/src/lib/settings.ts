import Building2Icon from '@lucide/svelte/icons/building-2';
import KeyRoundIcon from '@lucide/svelte/icons/key-round';
import PaletteIcon from '@lucide/svelte/icons/palette';
import ShieldUserIcon from '@lucide/svelte/icons/shield-user';
import ShieldCheckIcon from '@lucide/svelte/icons/shield-check';
import UsersRoundIcon from '@lucide/svelte/icons/users-round';
import WebhookIcon from '@lucide/svelte/icons/webhook';
import type { CurrentUser, OrganizationRole, Permission, RoleSummary } from '@screenhire/api-client';
import { can } from '#lib/permissions.js';
import type { LucideIcon } from '#lib/shell.js';

/**
 * The Settings menu's pages (design system section 11), each with the permission its changes need: the sidebar's one
 * Settings item opens the first the person may use, and the command palette lists those. Kept apart from `sections`,
 * which every member sees and which the G chords read. Email templates live on the Templates page (section 22), and
 * their old addresses here redirect there.
 */
export const settingsSections: readonly { label: string; href: string; icon: LucideIcon; permission: Permission }[] = [
  { label: 'Organization', href: '/settings/organization', icon: Building2Icon, permission: 'EditSettings' },
  { label: 'Team', href: '/settings/team', icon: UsersRoundIcon, permission: 'ManageTeam' },
  { label: 'Roles', href: '/settings/roles', icon: ShieldUserIcon, permission: 'ManageTeam' },
  { label: 'Branding', href: '/settings/branding', icon: PaletteIcon, permission: 'EditSettings' },
  { label: 'Integrity', href: '/settings/integrity', icon: ShieldCheckIcon, permission: 'EditSettings' },
  { label: 'API keys', href: '/settings/api-keys', icon: KeyRoundIcon, permission: 'ManageApiKeys' },
  { label: 'Webhooks', href: '/settings/webhooks', icon: WebhookIcon, permission: 'ManageWebhooks' },
];

/** The Settings pages the person's role may use: none means no Settings item at all. */
export const visibleSettingsSections = (user: CurrentUser | null | undefined) =>
  settingsSections.filter((section) => can(user, section.permission));

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
