import BookmarkIcon from '@lucide/svelte/icons/bookmark';
import BriefcaseIcon from '@lucide/svelte/icons/briefcase';
import ChartBarIcon from '@lucide/svelte/icons/chart-bar';
import LayoutTemplateIcon from '@lucide/svelte/icons/layout-template';
import ListTodoIcon from '@lucide/svelte/icons/list-todo';
import UsersIcon from '@lucide/svelte/icons/users';
import type { ConnectionStatus } from '@screenhire/realtime';
import { getContext, setContext } from 'svelte';

/** What the signed-in shell offers the pages inside it. */
export interface Shell {
  /** Opens the command palette (Ctrl K / ⌘K). */
  openPalette(): void;
  /** Opens the keyboard shortcuts dialog (`?`). */
  openShortcuts(): void;
  /** The hub connection's state, for the top bar's Live indicator. */
  live(): ConnectionStatus;
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
  // The candidates the team kept in mind for future roles (docs/architecture/talent-pool.md).
  { label: 'Talent pool', href: '/talent-pool', key: 'p', icon: BookmarkIcon, matches: (path) => path.startsWith('/talent-pool') },
  { label: 'Templates', href: '/templates', key: 't', icon: LayoutTemplateIcon, matches: (path) => path.startsWith('/templates') },
  { label: 'My tasks', href: '/tasks', key: 'm', icon: ListTodoIcon, matches: (path) => path.startsWith('/tasks') },
  { label: 'Reports', href: '/reports', key: 'r', icon: ChartBarIcon, matches: (path) => path.startsWith('/reports') },
];
