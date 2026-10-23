<script lang="ts">
  import { getApplication, listAutomationRuns } from '@screenhire/api-client';
  import { createListApplications } from '@screenhire/api-client/svelte-query';
  import { setQueryClientContext } from '@tanstack/svelte-query';
  import { onDestroy } from 'svelte';
  import { MediaQuery } from 'svelte/reactivity';
  import { goto } from '$app/navigation';
  import { clock } from '#lib/clock.svelte.js';
  import AppSidebar from '#lib/components/app-sidebar.svelte';
  import CommandPalette from '#lib/components/command-palette.svelte';
  import NotificationTray from '#lib/components/notification-tray.svelte';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { Notifications, teamNotification } from '#lib/notifications.svelte.js';
  import { createQueryClient } from '#lib/query.js';
  import { startLiveUpdates } from '#lib/realtime.js';
  import { savedViews } from '#lib/saved-views.js';
  import { sections, setShell } from '#lib/shell.js';
  import { createChordHandler } from '#lib/shortcuts.js';
  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps = $props();

  // One query cache and one hub connection for the signed-in shell. Leaving it (signing out) drops both, so nothing
  // cached for this account outlives the session.
  const queryClient = createQueryClient();
  setQueryClientContext(queryClient);
  const live = startLiveUpdates(queryClient);

  // A NotifyTeam action shows in the tray. TeamNotified carries ids only, so the tray reads the application and the run
  // (for the action's note) through the API first; an application this person cannot read shows nothing.
  const notifications = new Notifications();
  const stopNotifying = live.onEvent((event) => {
    if (event.type === 'TeamNotified') void notifyTeam(event.data.applicationId, event.data.runId);
  });

  async function notifyTeam(applicationId: string, runId: string) {
    const [application, runs] = await Promise.all([getApplication(applicationId), listAutomationRuns(applicationId)]);
    if (application.status !== 200) return;
    const run = runs.status === 200 ? runs.data.find((entry) => entry.id === runId) : undefined;
    notifications.add(teamNotification(runId, run, application.data));
  }

  onDestroy(() => {
    stopNotifying();
    void live.stop();
    queryClient.clear();
  });

  let paletteOpen = $state(false);
  let shortcutsOpen = $state(false);
  setShell({ openPalette: () => (paletteOpen = true), openShortcuts: () => (shortcutsOpen = true) });

  const chords = createChordHandler(Object.fromEntries(sections.map((section) => [section.key, section.href])), (href) => void goto(href));

  function onkeydown(event: KeyboardEvent) {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      paletteOpen = !paletteOpen;
      return;
    }

    if (preferences.shortcutsEnabled && !event.defaultPrevented && chords(event)) {
      event.preventDefault();
    }
  }

  // Saved-view counts come from the same list the candidates page shows, so they agree and update live.
  const applications = createListApplications();
  const viewCounts = $derived(
    Object.fromEntries(
      savedViews.map((view) => [view.id, applications.data?.data.filter((application) => view.matches(application, clock.now)).length]),
    ),
  );

  // Tailwind's lg: from there up the sidebar is full; below it, the rail (md) or the phone sheet.
  const laptop = new MediaQuery('min-width: 1024px');
</script>

<!-- Capture, so a G chord sees its second key before a table's J / K handler can claim it. -->
<svelte:window onkeydowncapture={onkeydown} />

<!-- The full sidebar from lg, the icon rail at md, and a sheet behind the menu button on phones (design system section 8). -->
<Sidebar.Provider open={laptop.current}>
  <AppSidebar user={data.user} {viewCounts} live={live.connection.status} />
  <Sidebar.Inset class="min-w-0">
    {@render children()}
  </Sidebar.Inset>
</Sidebar.Provider>

<NotificationTray items={notifications.items} ondismiss={(id) => notifications.dismiss(id)} />
<CommandPalette bind:open={paletteOpen} />
<ShortcutsDialog bind:open={shortcutsOpen} />
