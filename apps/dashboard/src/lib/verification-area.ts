import type { CurrentUser } from '@screenhire/api-client';
import { can, seesAllJobs } from '#lib/permissions.js';

// The Verification area's tabs (design system section 7, Area; docs/architecture/verification-engine.md 10.11). One list
// serves the tab strip, each tab's address check and the tests, so a tab someone cannot use is left out everywhere the same
// way (A3) and its address sends them to the Overview. Four tabs, by what a setting affects: Checks holds every switch that decides
// what runs and the settings that tune each group, and AI use holds who pays and how much (the owner's walk-through, 2026-11-21).

export type VerificationTabValue = 'overview' | 'activity' | 'checks' | 'ai-use';

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

/** Edit settings changes the Checks tab and the model, and the alerts on both tabs. */
export const canEditVerification = (user: CurrentUser | null | undefined) => can(user, 'EditSettings');

/**
 * The AI use tab is for who can see what the checks used or choose whose key runs them. Each part of it checks its own
 * permission: usage needs `canSeeUsage`, the key and the alert need Edit settings, and the limits are set by an Owner.
 */
export const canOpenAiUse = (user: CurrentUser | null | undefined) => canSeeUsage(user) || canEditVerification(user);

export const verificationTabs: readonly VerificationTab[] = [
  { value: 'overview', label: 'Overview', href: '/verification', allowed: () => true, settings: false },
  { value: 'activity', label: 'Activity log', href: '/verification/activity', allowed: () => true, settings: false },
  { value: 'checks', label: 'Checks', href: '/verification/checks', allowed: () => true, settings: true },
  { value: 'ai-use', label: 'AI use', href: '/verification/ai-use', allowed: canOpenAiUse, settings: true },
];

/**
 * Where the addresses of the tabs that merged now lead. Bell notifications, alert emails and Slack messages sent before the
 * change still link to them. Notifications goes to Checks, which holds its first alert and which everyone can open.
 */
export const movedTabs = {
  '/verification/policy': '/verification/checks',
  '/verification/notifications': '/verification/checks',
  '/verification/usage': '/verification/ai-use',
  '/verification/ai-model': '/verification/ai-use',
} as const;

/** The tabs this person sees, in order. */
export const tabsFor = (user: CurrentUser | null | undefined) => verificationTabs.filter((tab) => tab.allowed(user));

/** The tab an address belongs to; the area's own address is the Overview. */
export function tabOfPath(pathname: string): VerificationTab {
  const path = pathname.replace(/\/+$/, '');
  return verificationTabs.find((tab) => tab.href !== '/verification' && (path === tab.href || path.startsWith(`${tab.href}/`))) ?? verificationTabs[0];
}

export const verificationTab = (value: VerificationTabValue) => verificationTabs.find((tab) => tab.value === value)!;
