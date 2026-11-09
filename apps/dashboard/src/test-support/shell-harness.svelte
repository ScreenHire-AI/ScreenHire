<script lang="ts">
  import type { CurrentUser, SavedViewSummary } from '@screenhire/api-client';
  import { MediaQuery } from 'svelte/reactivity';
  import AppSidebar from '#lib/components/app-sidebar.svelte';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
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
    saved = [],
    hiddenBuiltIns = [],
    viewActions,
  }: {
    user: CurrentUser;
    crumbs?: readonly { label: string; href?: string }[];
    back?: boolean;
    /** The views people saved, and the built-in ones this person hid, as the views query returns them. */
    saved?: SavedViewSummary[];
    hiddenBuiltIns?: string[];
    viewActions?: ViewActions;
    /** The person's open tasks, as the layout counts them for the badge after My tasks. */
    openTasks?: number;
  } = $props();

  const views = $derived(sidebarViews({ saved, hiddenBuiltIns, jobTitles: {} }));

  let shortcutsOpen = $state(false);
  let notificationsOpen = $state(false);
  setShell({
    openPalette: () => {},
    openShortcuts: () => (shortcutsOpen = true),
    openNotifications: () => (notificationsOpen = true),
    openCalendar: () => {},
    live: () => 'connected',
    features: () => ({ slack: true }),
    sendingDomainMark: () => undefined,
  });
  const laptop = new MediaQuery('min-width: 1024px');
</script>

<!-- The root layout's tooltip provider. -->
<Tooltip.Provider>
  <Sidebar.Provider open={laptop.current}>
    <AppSidebar {user} {views} {viewActions} {openTasks} />
    <Sidebar.Inset>
      <TopBar {crumbs} {back} />
      <main><h1>Page</h1></main>
    </Sidebar.Inset>
  </Sidebar.Provider>
  <ShortcutsDialog bind:open={shortcutsOpen} />
  <!-- The sheet itself needs the query cache; the harness shows only that the menu opens it. -->
  {#if notificationsOpen}<p>Notifications sheet open</p>{/if}
</Tooltip.Provider>
