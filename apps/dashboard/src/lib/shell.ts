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

/** The sections in the sidebar, with their "go to" shortcut (G, then the letter). */
export const sections = [
  { label: 'Jobs', href: '/', key: 'j', matches: (path: string) => path === '/' || path.startsWith('/jobs/') },
  {
    label: 'Candidates',
    href: '/candidates',
    key: 'c',
    matches: (path: string) => path.startsWith('/candidates') || path.startsWith('/applications/'),
  },
  { label: 'Templates', href: '/templates', key: 't', matches: (path: string) => path.startsWith('/templates') },
] as const;
