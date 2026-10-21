import BriefcaseIcon from '@lucide/svelte/icons/briefcase';
import LayoutTemplateIcon from '@lucide/svelte/icons/layout-template';
import UsersIcon from '@lucide/svelte/icons/users';
import { getContext, setContext } from 'svelte';

/** What the signed-in shell offers the pages inside it. */
export interface Shell {
  /** Opens the command palette (Ctrl K / ⌘K). */
  openPalette(): void;
  /** Opens the keyboard shortcuts dialog (`?`). */
  openShortcuts(): void;
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
  { label: 'Templates', href: '/templates', key: 't', icon: LayoutTemplateIcon, matches: (path) => path.startsWith('/templates') },
];
