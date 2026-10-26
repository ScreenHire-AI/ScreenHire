<script lang="ts">
  import type { CurrentUser } from '@screenhire/api-client';
  import { MediaQuery } from 'svelte/reactivity';
  import AppSidebar from '#lib/components/app-sidebar.svelte';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { setShell } from '#lib/shell.js';

  // The signed-in shell's frame as the (app) layout wires it (sidebar, top bar, shortcuts dialog), without its query
  // cache and hub connection, for component tests.
  let {
    user,
    crumbs = [{ label: 'Candidates' }],
    back,
    viewCounts = {},
  }: {
    user: CurrentUser;
    crumbs?: readonly { label: string; href?: string }[];
    back?: boolean;
    viewCounts?: Record<string, number | undefined>;
  } = $props();

  let shortcutsOpen = $state(false);
  setShell({ openPalette: () => {}, openShortcuts: () => (shortcutsOpen = true) });
  const laptop = new MediaQuery('min-width: 1024px');
</script>

<!-- The root layout's tooltip provider. -->
<Tooltip.Provider>
  <Sidebar.Provider open={laptop.current}>
    <AppSidebar {user} {viewCounts} live="connected" />
    <Sidebar.Inset>
      <TopBar {crumbs} {back} />
      <main><h1>Page</h1></main>
    </Sidebar.Inset>
  </Sidebar.Provider>
  <ShortcutsDialog bind:open={shortcutsOpen} />
</Tooltip.Provider>
