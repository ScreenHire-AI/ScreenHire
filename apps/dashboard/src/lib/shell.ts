import BookmarkIcon from '~icons/lucide/bookmark';
import BriefcaseIcon from '~icons/lucide/briefcase';
import ChartBarIcon from '~icons/lucide/chart-bar';
import LayoutTemplateIcon from '~icons/lucide/layout-template';
import ListTodoIcon from '~icons/lucide/list-todo';
import ShieldCheckIcon from '~icons/lucide/shield-check';
import UsersIcon from '~icons/lucide/users';
import type { ConnectionStatus } from '@screenhire/realtime';
import type { CurrentUser, Permission } from '@screenhire/api-client';
import { getContext, setContext } from 'svelte';
import { can } from '#lib/permissions.js';
import type { SendingDomainMark } from '#lib/sending-domain.js';
import type { SettingsFeatures } from '#lib/settings.js';

/** What the signed-in shell offers the pages inside it. */
export interface Shell {
  /** Opens the command palette (Ctrl K / ⌘K). */
  openPalette(): void;
  /** Opens the keyboard shortcuts dialog (`?`). */
  openShortcuts(): void;
  /** Opens the Notifications sheet: which teammates' changes show as toasts. */
  openNotifications(): void;
  /** Opens the Calendar sheet from the account menu, for a page that needs the person's calendar connected. */
  openCalendar(): void;
  /** The hub connection's state, for the top bar's Live indicator. */
  live(): ConnectionStatus;
  /** Which optional features this installation has (Slack today); a feature not heard from yet is missing, and counts as off. */
  features(): SettingsFeatures;
  /** The signed-in person, for the parts of the top bar that depend on what they may do (the notification bell's setup steps). */
  user(): CurrentUser | null | undefined;
  /** The status the Settings home and menu show beside Email domain while something is left to do; missing when verified or until heard. */
  sendingDomainMark(): SendingDomainMark | undefined;
}

const key = Symbol('shell');

export const setShell = (shell: Shell) => setContext(key, shell);

export const getShell = () => getContext<Shell>(key);

/** A Lucide icon component (design system section 14). */
export type LucideIcon = typeof BriefcaseIcon;

/**
 * The sections the dashboard goes to, with their icon and "go to" shortcut (G, then the letter). The sidebar lists each
 * one unless `sidebar` is false: Saved candidates is a choice on the Candidates page, but G S, Ctrl K and the `?` dialog
 * still go to it, so they read this list too.
 */
export const sections: readonly {
  label: string;
  href: string;
  key: string;
  icon: LucideIcon;
  matches: (path: string) => boolean;
  /** False for a destination that is not its own sidebar item. */
  sidebar?: false;
  /** The permission that shows it: the sidebar, G chord, Ctrl K and the `?` dialog leave it out for anyone without. */
  permission?: Permission;
}[] = [
  { label: 'Jobs', href: '/', key: 'j', icon: BriefcaseIcon, matches: (path) => path === '/' || path.startsWith('/jobs/') },
  {
    label: 'Candidates',
    href: '/candidates',
    key: 'c',
    icon: UsersIcon,
    matches: (path) => path.startsWith('/candidates') || path.startsWith('/applications/'),
  },
  // The candidates the team saved, until they remove them or the candidate asks to stop (docs/architecture/talent-pool.md).
  // They are the Saved choice on the Candidates page, so the sidebar's Candidates item is the active one there.
  { label: 'Saved candidates', href: '/candidates?list=saved', key: 's', icon: BookmarkIcon, matches: () => false, sidebar: false },
  { label: 'Templates', href: '/templates', key: 't', icon: LayoutTemplateIcon, matches: (path) => path.startsWith('/templates') },
  { label: 'My tasks', href: '/tasks', key: 'm', icon: ListTodoIcon, matches: (path) => path.startsWith('/tasks') },
  { label: 'Reports', href: '/reports', key: 'r', icon: ChartBarIcon, matches: (path) => path.startsWith('/reports') },
  // How the checks on applicants work and what they found. Everyone who can see verification has the area; each of its tabs
  // checks its own permission (`#lib/verification-area.ts`), so the sidebar item carries none.
  { label: 'Verification', href: '/verification', key: 'v', icon: ShieldCheckIcon, matches: (path) => path.startsWith('/verification') },
];

/** The sections this person may go to: one that names a permission only for a role that has it (none does today). */
export const sectionsFor = (user: CurrentUser | null | undefined) => sections.filter((section) => !section.permission || can(user, section.permission));
