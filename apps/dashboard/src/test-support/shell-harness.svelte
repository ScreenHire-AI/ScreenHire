<script lang="ts">
  import type { CurrentUser, SavedViewSummary } from '@screenhire/api-client';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import { type ComponentProps } from 'svelte';
  import { MediaQuery } from 'svelte/reactivity';
  import AppSidebar from '#lib/components/app-sidebar.svelte';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import type { SettingsFeatures } from '#lib/settings.js';
  import { setShell } from '#lib/shell.js';
  import { sidebarViews } from '#lib/sidebar-views.js';
  import type { ViewActions } from '#lib/views.svelte.js';

  // The signed-in shell's frame as the (app) layout wires it (sidebar, top bar, shortcuts dialog), without its query
  // cache and hub connection, for component tests.
  let {
    user,
    crumbs = [{ label: 'Candidates' }],
    back,
    openTasks,
    trialEndsAt,
    saved = [],
    hiddenBuiltIns = [],
    viewActions,
    teamViews,
    features = { slack: true },
  }: {
    user: CurrentUser;
    crumbs?: readonly { label: string; href?: string }[];
    back?: boolean;
    /** The views people saved, and the built-in ones this person hid, as the views query returns them. */
    saved?: SavedViewSummary[];
    hiddenBuiltIns?: string[];
    viewActions?: ViewActions;
    /** What the sheet of team views lists; without it the sidebar has no Browse team views button. */
    teamViews?: ComponentProps<typeof AppSidebar>['teamViews'];
    /** The person's open tasks, as the layout counts them for the badge after My tasks. */
    openTasks?: number;
    /** When the trial ends, as the layout works it out for an Owner; missing for everyone else. */
    trialEndsAt?: string | null;
    /** Which optional features the shell has heard are on. */
    features?: SettingsFeatures;
  } = $props();

  // The top bar's notification bell reads the notifications and the setup answers. No retries, so a request nobody stubbed fails at once.
  setQueryClientContext(new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } }));

  const views = $derived(sidebarViews({ saved, hiddenBuiltIns, sidebarOrder: saved.filter((view) => view.inSidebar && !view.mine).map((view) => view.id), jobTitles: {} }));

  let shortcutsOpen = $state(false);
  setShell({
    openPalette: () => {},
    openShortcuts: () => (shortcutsOpen = true),
    openNotifications: () => {},
    openCalendar: () => {},
    live: () => 'connected',
    user: () => user,
    features: () => features,
    sendingDomainMark: () => undefined,
  });
  const laptop = new MediaQuery('min-width: 1024px');
</script>

<!-- The root layout's tooltip provider. -->
<Tooltip.Provider>
  <Sidebar.Provider open={laptop.current}>
    <AppSidebar {user} {views} {viewActions} {teamViews} {openTasks} {trialEndsAt} />
    <Sidebar.Inset>
      <TopBar {crumbs} {back} />
      <main><h1>Page</h1></main>
    </Sidebar.Inset>
  </Sidebar.Provider>
  <ShortcutsDialog bind:open={shortcutsOpen} />
  <!-- The sheet itself needs the query cache; the harness shows only that the menu opens it. -->
</Tooltip.Provider>
