import BookmarkIcon from '~icons/lucide/bookmark';
import BriefcaseIcon from '~icons/lucide/briefcase';
import ChartBarIcon from '~icons/lucide/chart-bar';
import LayoutTemplateIcon from '~icons/lucide/layout-template';
import ListTodoIcon from '~icons/lucide/list-todo';
import UsersIcon from '~icons/lucide/users';
import type { ConnectionStatus } from '@screenhire/realtime';
import { getContext, setContext } from 'svelte';
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
}

const key = Symbol('shell');

export const setShell = (shell: Shell) => setContext(key, shell);

export const getShell = () => getContext<Shell>(key);

/** A Lucide icon component (design system section 14). */
export type LucideIcon = typeof BriefcaseIcon;

/** The sections in the sidebar, with their icon and "go to" shortcut (G, then the letter). */
export const sections: readonly {
  label: string;
  href: string;
  key: string;
  icon: LucideIcon;
  matches: (path: string) => boolean;
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
  { label: 'Saved candidates', href: '/saved', key: 's', icon: BookmarkIcon, matches: (path) => path.startsWith('/saved') },
  { label: 'Templates', href: '/templates', key: 't', icon: LayoutTemplateIcon, matches: (path) => path.startsWith('/templates') },
  { label: 'My tasks', href: '/tasks', key: 'm', icon: ListTodoIcon, matches: (path) => path.startsWith('/tasks') },
  { label: 'Reports', href: '/reports', key: 'r', icon: ChartBarIcon, matches: (path) => path.startsWith('/reports') },
];
