import FileLockIcon from '~icons/lucide/file-lock';
import GlobeIcon from '~icons/lucide/globe';
import Building2Icon from '~icons/lucide/building-2';
import CreditCardIcon from '~icons/lucide/credit-card';
import PlugIcon from '~icons/lucide/plug';
import MailCheckIcon from '~icons/lucide/mail-check';
import PaletteIcon from '~icons/lucide/palette';
import ShieldUserIcon from '~icons/lucide/shield-user';
import ShieldCheckIcon from '~icons/lucide/shield-check';
import SlidersHorizontalIcon from '~icons/lucide/sliders-horizontal';
import UsersRoundIcon from '~icons/lucide/users-round';
import type { CurrentUser, JobAccess, OrganizationRole, Permission, RoleSummary } from '@screenhire/api-client';
import { integrations } from '#lib/integrations.js';
import { can, seesAllJobs } from '#lib/permissions.js';
import type { LucideIcon } from '#lib/shell.js';

/** The team's history and its Last updated (`/team/history`, `/team/last-update`): invalidate it with every team change. */
export const teamPath = '/v1/organization/team';

/** The three groups of Settings pages, one sidebar item each (`settingsAreas`). */
export type SettingsGroup = 'Organization' | 'Hiring setup' | 'Integrations';

/** The Integrations directory: every integration as a row, past search and filters by kind (design system S9). */
export const integrationsHref = '/settings/integrations';

/**
 * A feature the installation may not have. Slack needs a Slack app set up where ScreenHire is hosted (`GET /v1/slack` says
 * `available`), and the sending domain needs an email provider (`GET /v1/organization/sending-domain` says `available`), so
 * their pages and menu entries are hidden everywhere they are not (A3). Slack is off until the shell has heard that it is
 * on; the sending domain is on until the shell has heard that it is off, because nearly every installation has it and the
 * entry should not wait for a query to appear. Billing needs a payment provider (`GET /v1/organization/billing` says `available`), and
 * only an Owner may ask, so it is off until the shell has heard that it is on, like Slack.
 */
export type SettingsFeature = 'slack' | 'sendingDomain' | 'billing';
export type SettingsFeatures = Readonly<Partial<Record<SettingsFeature, boolean>>>;

const shownUntilOff: ReadonlySet<SettingsFeature> = new Set(['sendingDomain']);

/** Whether a feature's page and menu entry show now: what the shell heard, else the feature's own default. */
export const featureOn = (features: SettingsFeatures, feature: SettingsFeature) => features[feature] ?? shownUntilOff.has(feature);

/** Whether a page of this feature has to wait for the shell's first answer before it can be shown or refused. */
export const featureWaits = (features: SettingsFeatures, feature: SettingsFeature) => features[feature] === undefined && !shownUntilOff.has(feature);

/**
 * Slack's entry in `SettingsFeatures`, from the shell's `GET /v1/slack` query. The last answer stands through a failed
 * refetch, so a dropped connection never takes the page from someone using it. A status that was never read counts as no
 * Slack (A3), so its page does not wait forever; `undefined` means the first answer is still on its way.
 */
export const slackFeature = (status: { available: boolean } | undefined, failed: boolean) => status?.available ?? (failed ? false : undefined);

/** Billing's entry in `SettingsFeatures`, from the shell's `GET /v1/organization/billing` query: the same rule as Slack's. */
export const billingFeature = slackFeature;

/**
 * The Settings pages (design system section 11), each with the permission its changes need, in three groups that are
 * three sidebar items (`settingsAreas`): the organization and its people, how hiring is set up, and what connects to
 * it. Each item opens the first page the person may use, and the command palette lists them all. Kept apart from
 * `sections`, which every member sees and which the G chords read. Email templates live on the Templates page
 * (section 21), and their old addresses here redirect there.
 */
export const settingsSections: readonly {
  label: string;
  href: string;
  icon: LucideIcon;
  permission: Permission;
  group: SettingsGroup;
  allJobs?: true;
  /** The page exists only where this feature is set up (design system A3): hidden, not shown unavailable. */
  feature?: SettingsFeature;
  /** What a person whose role lacks `permission` reads in place of "Ask an owner or admin", for a page only some roles may open. */
  refusal?: string;
}[] = [
  // The group is Organization, so its first page is General: the name, where the data lives and when it was created.
  { label: 'General', href: '/settings/organization', icon: Building2Icon, permission: 'EditSettings', group: 'Organization' },
  { label: 'Team', href: '/settings/team', icon: UsersRoundIcon, permission: 'ManageTeam', group: 'Organization' },
  { label: 'Roles', href: '/settings/roles', icon: ShieldUserIcon, permission: 'ManageTeam', group: 'Organization' },
  // A request is about one candidate whatever job they applied to, and retention deletes across every job, so the page
  // needs Erase candidates and all jobs, as the API does.
  { label: 'Data requests', href: '/settings/data-requests', icon: FileLockIcon, permission: 'EraseCandidates', group: 'Organization', allJobs: true },
  // Owners only (ManageBilling), and only where a payment provider is set up, so it is hidden everywhere else (A3).
  {
    label: 'Billing',
    href: '/settings/billing',
    icon: CreditCardIcon,
    permission: 'ManageBilling',
    group: 'Organization',
    feature: 'billing',
    refusal: 'Only an owner of your organization can change billing.',
  },
  { label: 'Careers site', href: '/settings/careers-site', icon: GlobeIcon, permission: 'EditSettings', group: 'Hiring setup' },
  { label: 'Branding', href: '/settings/branding', icon: PaletteIcon, permission: 'EditSettings', group: 'Hiring setup' },
  { label: 'Verification', href: '/settings/verification', icon: ShieldCheckIcon, permission: 'EditSettings', group: 'Hiring setup' },
  { label: 'Email domain', href: '/settings/sending-domain', icon: MailCheckIcon, permission: 'EditSettings', group: 'Hiring setup', feature: 'sendingDomain' },
  // The Integrations group is the integrations list, so a new integration is one entry there (#lib/integrations.ts).
  ...integrations.map(({ label, href, icon, permission, allJobs, feature }) => ({
    label,
    href,
    icon,
    permission,
    group: 'Integrations' as const,
    ...(allJobs && { allJobs }),
    ...(feature && { feature }),
  })),
];

/**
 * The three items the sidebar shows in place of one Settings item, each opening the first page the person may use in its
 * group (Integrations opens its directory). A group's pages share the Settings menu, except Integrations, whose pages are
 * records of the directory. An item shows for anyone whose role has a permission for at least one of its pages.
 */
export const settingsAreas: readonly { group: SettingsGroup; label: string; icon: LucideIcon; href?: string }[] = [
  { group: 'Organization', label: 'Organization', icon: Building2Icon },
  { group: 'Hiring setup', label: 'Hiring setup', icon: SlidersHorizontalIcon },
  { group: 'Integrations', label: 'Integrations', icon: PlugIcon, href: integrationsHref },
];

/**
 * The Settings pages the person's role may use: none means no Settings item at all. API keys, webhooks, Slack and Data
 * requests reach every job, so they also need all jobs (ADR-0015); the API refuses them otherwise with 403 "Needs all jobs". A page of a
 * feature that is not set up (Slack) is left out whoever asks.
 */
export const visibleSettingsSections = (user: CurrentUser | null | undefined, features: SettingsFeatures = {}) =>
  settingsSections.filter(
    (section) => can(user, section.permission) && (!section.allJobs || seesAllJobs(user)) && (!section.feature || featureOn(features, section.feature)),
  );

/** The Settings page a path belongs to, such as Roles for `/settings/roles/compare`. */
export const settingsSectionOf = (path: string) =>
  settingsSections.find((section) => path === section.href || path.startsWith(`${section.href}/`));

/** The integrations this person may open: the directory lists them, and a feature that is off leaves its entry out (A3). */
export const visibleIntegrations = (user: CurrentUser | null | undefined, features: SettingsFeatures = {}) => {
  const hrefs = new Set(visibleSettingsSections(user, features).map((section) => section.href));
  return integrations.filter((integration) => hrefs.has(integration.href));
};

/** Whether the Integrations directory is open to this person. It has no permission of its own: one page it lists must be. */
export const directoryVisible = (user: CurrentUser | null | undefined, features: SettingsFeatures = {}) =>
  visibleSettingsSections(user, features).some((section) => section.group === 'Integrations');

/** The sidebar items to show: the areas that have a page this person may use, each with where it opens. */
export const visibleSettingsAreas = (user: CurrentUser | null | undefined, features: SettingsFeatures = {}) => {
  const pages = visibleSettingsSections(user, features);
  return settingsAreas.flatMap((area) => {
    const first = pages.find((page) => page.group === area.group);
    return first ? [{ ...area, href: area.href ?? first.href }] : [];
  });
};

/** The area a path belongs to: its page's group, or Integrations for the directory itself. */
export const settingsAreaOf = (path: string): SettingsGroup | undefined =>
  path === integrationsHref ? 'Integrations' : settingsSectionOf(path)?.group;

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
