import type { CurrentUser } from '@screenhire/api-client';
import { can, seesAllJobs } from '#lib/permissions.js';

// The Verification area's tabs (design system section 7, Area; docs/architecture/verification-engine.md 10.11). One list
// serves the tab strip, each tab's address check and the tests, so a tab someone cannot use is left out everywhere the same
// way (A3) and its address sends them to the Overview.

export type VerificationTabValue = 'overview' | 'activity' | 'usage' | 'checks' | 'policy' | 'ai-model' | 'notifications';

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

/** Edit settings changes Checks, Policy and the model, and opens the AI model and Notifications tabs. */
export const canEditVerification = (user: CurrentUser | null | undefined) => can(user, 'EditSettings');

export const verificationTabs: readonly VerificationTab[] = [
  { value: 'overview', label: 'Overview', href: '/verification', allowed: () => true, settings: false },
  { value: 'activity', label: 'Activity log', href: '/verification/activity', allowed: () => true, settings: false },
  { value: 'usage', label: 'Usage', href: '/verification/usage', allowed: canSeeUsage, settings: false },
  { value: 'checks', label: 'Checks', href: '/verification/checks', allowed: () => true, settings: true },
  { value: 'policy', label: 'Policy', href: '/verification/policy', allowed: () => true, settings: true },
  { value: 'ai-model', label: 'AI model', href: '/verification/ai-model', allowed: canEditVerification, settings: true },
  { value: 'notifications', label: 'Notifications', href: '/verification/notifications', allowed: canEditVerification, settings: true },
];

/** The tabs this person sees, in order. */
export const tabsFor = (user: CurrentUser | null | undefined) => verificationTabs.filter((tab) => tab.allowed(user));

/** The tab an address belongs to; the area's own address is the Overview. */
export function tabOfPath(pathname: string): VerificationTab {
  const path = pathname.replace(/\/+$/, '');
  return verificationTabs.find((tab) => tab.href !== '/verification' && (path === tab.href || path.startsWith(`${tab.href}/`))) ?? verificationTabs[0];
}

export const verificationTab = (value: VerificationTabValue) => verificationTabs.find((tab) => tab.value === value)!;
