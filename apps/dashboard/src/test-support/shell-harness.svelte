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
    viewCounts = {},
    saved = [],
    hiddenBuiltIns = [],
    viewActions,
  }: {
    user: CurrentUser;
    crumbs?: readonly { label: string; href?: string }[];
    back?: boolean;
    /** Rows in each view, by view id; the layout derives them from the applications. */
    viewCounts?: Record<string, number | undefined>;
    /** The views people saved, and the built-in ones this person hid, as the views query returns them. */
    saved?: SavedViewSummary[];
    hiddenBuiltIns?: string[];
    viewActions?: ViewActions;
  } = $props();

  const listed = $derived(sidebarViews({ saved, hiddenBuiltIns, applications: undefined, now: 0, jobTitles: {} }));
  const withCount = (view: (typeof listed.visible)[number]) => ({ ...view, count: viewCounts[view.key] });
  const views = $derived({ visible: listed.visible.map(withCount), hidden: listed.hidden.map(withCount) });

  let shortcutsOpen = $state(false);
  let notificationsOpen = $state(false);
  setShell({
    openPalette: () => {},
    openShortcuts: () => (shortcutsOpen = true),
    openNotifications: () => (notificationsOpen = true),
    live: () => 'connected',
  });
  const laptop = new MediaQuery('min-width: 1024px');
</script>

<!-- The root layout's tooltip provider. -->
<Tooltip.Provider>
  <Sidebar.Provider open={laptop.current}>
    <AppSidebar {user} {views} {viewActions} />
    <Sidebar.Inset>
      <TopBar {crumbs} {back} />
      <main><h1>Page</h1></main>
    </Sidebar.Inset>
  </Sidebar.Provider>
  <ShortcutsDialog bind:open={shortcutsOpen} />
  <!-- The sheet itself needs the query cache; the harness shows only that the menu opens it. -->
  {#if notificationsOpen}<p>Notifications sheet open</p>{/if}
</Tooltip.Provider>
