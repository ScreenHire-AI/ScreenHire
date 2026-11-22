import type { CurrentUser } from '@screenhire/api-client';
import { can, seesAllJobs } from '#lib/permissions.js';

// The Verification area's tabs (design system section 7, Area; docs/architecture/verification-engine.md 10.11). One list
// serves the tab strip, each tab's address check and the tests, so a tab someone cannot use is left out everywhere the same
// way (A3) and its address sends them to the Overview. Three tabs: the Overview, the Activity log, and Checks, which holds every
// switch that decides what runs and the settings that tune each group. What the checks cost lives in Billing, whose key runs the AI
// check in Settings > Integrations > AI model, and the alerts in Your account and Settings > Notification defaults (the owner's
// walk-through, 2026-11-22).

export type VerificationTabValue = 'overview' | 'activity' | 'checks';

export interface VerificationTab {
  value: VerificationTabValue;
  label: string;
  href: string;
  /** Whether this person's role may open the tab. */
  allowed: (user: CurrentUser | null | undefined) => boolean;
  /** The tab shows the organization's saved verification settings, so the band shows when they last changed. */
  settings: boolean;
}

/**
 * View AI usage covers every job's calls, so it needs All jobs as well, whatever a role holds (RolePermissions.NeedsAllJobs).
 * Usage, and a model call's cost and provider request id, are for who has both.
 */
export const canSeeUsage = (user: CurrentUser | null | undefined) => can(user, 'ViewAiUsage') && seesAllJobs(user);

/** Edit settings changes the Checks tab, the AI model and the organization's notification defaults. */
export const canEditVerification = (user: CurrentUser | null | undefined) => can(user, 'EditSettings');

export const verificationTabs: readonly VerificationTab[] = [
  { value: 'overview', label: 'Overview', href: '/verification', allowed: () => true, settings: false },
  { value: 'activity', label: 'Activity log', href: '/verification/activity', allowed: () => true, settings: false },
  { value: 'checks', label: 'Checks', href: '/verification/checks', allowed: () => true, settings: true },
];

/** Billing's AI use section: what the checks used and cost, and the monthly limits. For who can see AI usage. */
export const aiUseAddress = '/settings/billing#ai-use';

/** Settings > Integrations > AI model: whose key runs the AI check. For Edit settings. */
export const aiModelAddress = '/settings/ai-model';

/** Settings > Organization > Notification defaults: the organization's alert settings. For Edit settings. */
export const notificationDefaultsAddress = '/settings/notification-defaults';

type User = CurrentUser | null | undefined;

// What an AI use address leads to: the usage for who sees it, else the key for who sets it, else the Overview.
const aiUseFor = (user: User) => (canSeeUsage(user) ? aiUseAddress : canEditVerification(user) ? aiModelAddress : '/verification');

/**
 * Where the addresses of tabs that moved now lead, for this person: bell notifications, alert emails, Slack messages and
 * bookmarks made before the change still link to them. Each goes to the page that holds what the tab held, and to the next
 * one this person may open where their role cannot use the first. A target may carry a hash, which the redirect keeps.
 */
export const movedTabs = {
  '/verification/policy': () => '/verification/checks',
  '/verification/notifications': (user: User) => (canEditVerification(user) ? notificationDefaultsAddress : '/account/notifications'),
  '/verification/usage': aiUseFor,
  '/verification/ai-use': aiUseFor,
  '/verification/ai-model': (user: User) => (canEditVerification(user) ? aiModelAddress : aiUseFor(user)),
} as const satisfies Record<string, (user: User) => string>;

export type MovedTab = keyof typeof movedTabs;

/** The tabs this person sees, in order. */
export const tabsFor = (user: CurrentUser | null | undefined) => verificationTabs.filter((tab) => tab.allowed(user));

/** The tab an address belongs to; the area's own address is the Overview. */
export function tabOfPath(pathname: string): VerificationTab {
  const path = pathname.replace(/\/+$/, '');
  return verificationTabs.find((tab) => tab.href !== '/verification' && (path === tab.href || path.startsWith(`${tab.href}/`))) ?? verificationTabs[0];
}

export const verificationTab = (value: VerificationTabValue) => verificationTabs.find((tab) => tab.value === value)!;
