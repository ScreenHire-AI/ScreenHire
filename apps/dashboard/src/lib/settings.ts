import type { CurrentUser, JobAccess, OrganizationRole, Permission, RoleSummary } from '@screenhire/api-client';
import { integrations } from '#lib/integrations.js';
import { can, seesAllJobs } from '#lib/permissions.js';

/** The team's history and its Last updated (`/team/history`, `/team/last-update`): invalidate it with every team change. */
export const teamPath = '/v1/organization/team';

/** The Settings home: every setting the person's role may use, with search (design system section 11). */
export const settingsHome = '/settings';

/** The three groups of Settings pages, in the order the Settings home lists them. */
export type SettingsGroup = 'Organization' | 'Careers site' | 'Integrations';
export const settingsGroupNames: readonly SettingsGroup[] = ['Organization', 'Careers site', 'Integrations'];

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

export interface SettingsSection {
  label: string;
  href: string;
  permission: Permission;
  group: SettingsGroup;
  /** One line on what it does, only where the name does not explain itself (design system W9). */
  description?: string;
  /** Everyday words that find it in Settings search and Ctrl K ("logo" finds Branding), kept beside the setting. */
  keywords: readonly string[];
  allJobs?: true;
  /** The page exists only where this feature is set up (design system A3): hidden, not shown unavailable. */
  feature?: SettingsFeature;
  /** What a person whose role lacks `permission` reads in place of "Ask an owner or admin", for a page only some roles may open. */
  refusal?: string;
}

/**
 * The Settings pages (design system section 11), each with the permission its changes need, in three groups: the
 * organization and its people, the careers site, and what connects to it. The one Settings item in the sidebar opens
 * the Settings home, which lists them all; a page's menu lists its group's pages, and the command palette finds each by
 * name, description and keywords. Kept apart from `sections`, which every member sees and which the G chords read.
 * Email templates live on the Templates page (section 21), and their old addresses here redirect there. Verification
 * is a sidebar item of its own (`/verification`).
 */
export const settingsSections: readonly SettingsSection[] = [
  // The group is Organization, so its first page is General: the name, where the data lives and when it was created.
  {
    label: 'General',
    href: '/settings/organization',
    description: 'Your name and where your data lives',
    keywords: ['name', 'organization', 'company', 'data region', 'region', 'location', 'created'],
    permission: 'EditSettings',
    group: 'Organization',
  },
  {
    label: 'Team',
    href: '/settings/team',
    keywords: ['invite', 'members', 'people', 'colleague', 'teammate', 'access', 'remove'],
    permission: 'ManageTeam',
    group: 'Organization',
  },
  {
    label: 'Roles',
    href: '/settings/roles',
    keywords: ['permissions', 'access', 'admin', 'recruiter', 'interviewer', 'custom role', 'invite'],
    permission: 'ManageTeam',
    group: 'Organization',
  },
  {
    label: 'Email domain',
    href: '/settings/sending-domain',
    description: 'Send email from your own address',
    keywords: ['sending domain', 'dns', 'sender', 'from address', 'dkim', 'spf', 'verify domain', 'mail'],
    permission: 'EditSettings',
    group: 'Organization',
    feature: 'sendingDomain',
  },
  // A request is about one candidate whatever job they applied to, and retention deletes across every job, so the page
  // needs Erase candidates and all jobs, as the API does.
  {
    label: 'Data requests',
    href: '/settings/data-requests',
    description: 'Candidates asking to see or erase their data',
    keywords: ['privacy', 'gdpr', 'erase', 'delete', 'retention', 'export', 'subject access'],
    permission: 'EraseCandidates',
    group: 'Organization',
    allJobs: true,
  },
  // Owners only (ManageBilling), and only where a payment provider is set up, so it is hidden everywhere else (A3).
  {
    label: 'Billing',
    href: '/settings/billing',
    keywords: ['plan', 'card', 'invoice', 'invoices', 'payment', 'subscription', 'trial', 'upgrade', 'price'],
    permission: 'ManageBilling',
    group: 'Organization',
    feature: 'billing',
    refusal: 'Only an owner of your organization can change billing.',
  },
  {
    label: 'Careers page',
    href: '/settings/careers-site',
    description: 'Your jobs page and its embed code',
    keywords: ['careers site', 'address', 'url', 'embed', 'widget', 'iframe', 'apply form', 'questions per page'],
    permission: 'EditSettings',
    group: 'Careers site',
  },
  {
    label: 'Branding',
    href: '/settings/branding',
    keywords: ['logo', 'colour', 'color', 'font', 'favicon', 'theme', 'brand', 'sharing image'],
    permission: 'EditSettings',
    group: 'Careers site',
  },
  // The Integrations group is the integrations list, so a new integration is one entry there (#lib/integrations.ts).
  ...integrations.map(({ label, href, description, keywords, permission, allJobs, feature }) => ({
    label,
    href,
    keywords,
    permission,
    group: 'Integrations' as const,
    ...(description && { description }),
    ...(allJobs && { allJobs }),
    ...(feature && { feature }),
  })),
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

/** The group a path belongs to: its page's group. The Settings home belongs to none. */
export const settingsGroupOf = (path: string): SettingsGroup | undefined => settingsSectionOf(path)?.group;

/** The pages grouped for the Settings home, in the groups' order; a group with no page left is left out (A3). */
export const settingsGroups = (sections: readonly SettingsSection[]) =>
  settingsGroupNames.flatMap((group) => {
    const pages = sections.filter((section) => section.group === group);
    return pages.length > 0 ? [{ group, sections: pages }] : [];
  });

/** What Settings search reads for a page: its name, its one line, its keywords and its group, lower case. */
export const settingsSearchText = (section: SettingsSection) =>
  [section.label, section.description ?? '', ...section.keywords, section.group].join(' ').toLowerCase();

/** Whether every word of `query` is in `text`, ignoring case. An empty query matches. A contains match, never fuzzy. */
export const matchesWords = (text: string, query: string) => {
  const lower = text.toLowerCase();
  return query
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((word) => lower.includes(word));
};

/**
 * The pages a search finds: each word typed must be in the page's name, description, keywords or group, and the name
 * comes first. A contains match, never fuzzy (design system section 15). An empty search finds every page.
 */
export const searchSettings = <T extends SettingsSection>(sections: readonly T[], query: string): T[] => {
  const found = sections.filter((section) => matchesWords(settingsSearchText(section), query));
  const named = (section: T) => matchesWords(section.label, query);
  return [...found.filter(named), ...found.filter((section) => !named(section))];
};

/**
 * A settings page's breadcrumb: Settings, the group, then the page. The group is plain text because it has no page of
 * its own. With a `trail` (a record under the page: a role, a webhook endpoint) the page links to itself and the trail
 * follows.
 */
export const settingsCrumbs = (href: string, ...trail: readonly { label: string; href?: string }[]) => {
  const section = settingsSectionOf(href);
  return [
    { label: 'Settings', href: settingsHome },
    ...(section ? [{ label: section.group }, { label: section.label, ...(trail.length > 0 && { href: section.href }) }] : []),
    ...trail,
  ];
};

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
