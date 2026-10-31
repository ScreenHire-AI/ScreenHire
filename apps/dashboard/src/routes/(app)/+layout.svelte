<script lang="ts">
  import { getApplication, listAutomationRuns, listTeamMembers } from '@screenhire/api-client';
  import {
    createGetSlack,
    createListApplications,
    getGetApplicationQueryOptions,
    getGetHiringTemplateQueryOptions,
    getGetNotificationSettingsQueryOptions,
    getListApplicationTasksQueryOptions,
    getListMemberJobsQueryOptions,
    getListTeamMembersQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { setQueryClientContext } from '@tanstack/svelte-query';
  import { onDestroy } from 'svelte';
  import { MediaQuery } from 'svelte/reactivity';
  import { goto } from '$app/navigation';
  import { clock } from '#lib/clock.svelte.js';
  import AppSidebar from '#lib/components/app-sidebar.svelte';
  import CommandPalette from '#lib/components/command-palette.svelte';
  import NotificationSettingsSheet from '#lib/components/notification-settings-sheet.svelte';
  import NotificationTray from '#lib/components/notification-tray.svelte';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { mentionNotification, mentionNotificationId, Notifications, teamNotification } from '#lib/notifications.svelte.js';
  import { createQueryClient } from '#lib/query.js';
  import { startLiveUpdates } from '#lib/realtime.js';
  import { savedViews } from '#lib/saved-views.js';
  import { sections, setShell } from '#lib/shell.js';
  import { createChordHandler } from '#lib/shortcuts.js';
  import { defaultNotificationSettings } from '#lib/teammate-changes.js';
  import { TeammateToasts } from '#lib/teammate-toasts.js';
  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps = $props();

  // One query cache and one hub connection for the signed-in shell. Leaving it (signing out) drops both, so nothing
  // cached for this account outlives the session.
  const queryClient = createQueryClient();
  setQueryClientContext(queryClient);
  const live = startLiveUpdates(queryClient);

  // The person's settings for teammates' changes, kept fresh in the cache so a toast follows a change in the sheet.
  let closeToasts = true;
  const notifications = new Notifications({ closeToasts: () => closeToasts });
  async function readSettings() {
    try {
      const settings = (await queryClient.fetchQuery(getGetNotificationSettingsQueryOptions())).data;
      closeToasts = settings.closeToasts;
      return settings;
    } catch {
      // Unreadable settings are everything on, as for a member who never changed them.
      return defaultNotificationSettings;
    }
  }

  // A teammate's change to something this person works on shows as a toast (docs/architecture/realtime.md). The events
  // carry ids only; names come through the API, read through the shell's cache, so a burst of events reads the team once.
  const teammateToasts = new TeammateToasts(
    {
      userId: () => data.user?.id,
      members: async () => (await queryClient.fetchQuery(getListTeamMembersQueryOptions())).data,
      memberJobs: async (memberId) => (await queryClient.fetchQuery(getListMemberJobsQueryOptions(memberId))).data,
      settings: readSettings,
      application: async (applicationId) => (await queryClient.fetchQuery(getGetApplicationQueryOptions(applicationId))).data,
      tasks: async (applicationId) => (await queryClient.fetchQuery(getListApplicationTasksQueryOptions(applicationId))).data,
      templateName: async (templateId) => (await queryClient.fetchQuery(getGetHiringTemplateQueryOptions(templateId))).data.name,
    },
    (toast) => notifications.toast(toast),
  );
  const stopToasting = live.onEvent((event) => void teammateToasts.receive(event));
  // A NotifyTeam action shows in the tray. TeamNotified carries ids only, so the tray reads the application and the run
  // (for the action's note) through the API first; an application this person cannot read shows nothing.
  // A note that mentions this person shows in their tray only: the event reaches the whole organization, ids only. A
  // deleted note takes its card with it, so the card never links to a note that is gone.
  const stopNotifying = live.onEvent((event) => {
    if (event.type === 'TeamNotified') void notifyTeam(event.data.applicationId, event.data.runId);
    if (event.type === 'TeammateMentioned' && event.data.mentionedUserId === data.user?.id) {
      void notifyMention(event.data.applicationId, event.data.noteId, event.data.authorUserId);
    }
    if (event.type === 'ApplicationNoteDeleted') notifications.dismiss(mentionNotificationId(event.data.noteId));
  });

  async function notifyTeam(applicationId: string, runId: string) {
    try {
      const [application, runs] = await Promise.all([getApplication(applicationId), listAutomationRuns(applicationId)]);
      if (application.status !== 200) return;
      const run = runs.status === 200 ? runs.data.find((entry) => entry.id === runId) : undefined;
      notifications.add(teamNotification(runId, run, application.data));
    } catch {
      // The plain client throws only when the network is down; the run stays on the application's page, so the
      // notification is dropped rather than left as an unhandled rejection.
    }
  }

  async function notifyMention(applicationId: string, noteId: string, authorUserId: string | null) {
    try {
      const [application, team] = await Promise.all([getApplication(applicationId), listTeamMembers()]);
      if (application.status !== 200) return;
      const author = team.status === 200 ? team.data.find((member) => member.userId === authorUserId)?.displayName : undefined;
      notifications.add(mentionNotification(noteId, application.data, author));
    } catch {
      // As for NotifyTeam: the note stays on the application's page, and the email went out either way.
    }
  }

  onDestroy(() => {
    stopNotifying();
    stopToasting();
    teammateToasts.dispose();
    notifications.dispose();
    void live.stop();
    queryClient.clear();
  });

  let paletteOpen = $state(false);
  let shortcutsOpen = $state(false);
  let notificationsOpen = $state(false);
  let announcement = $state('');
  setShell({
    openPalette: () => (paletteOpen = true),
    openShortcuts: () => (shortcutsOpen = true),
    openNotifications: () => {
      // Cleared first, so the same sentence after a second save is a change a screen reader announces.
      announcement = '';
      notificationsOpen = true;
    },
    live: () => live.connection.status,
  });

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

  // Slack, for the Slack account item in each person's menu (docs/architecture/slack.md); it changes rarely.
  const slack = createGetSlack(() => ({ query: { enabled: Boolean(data.user?.organization), staleTime: 60_000 } }));

  // Tailwind's lg: from there up the sidebar is full; below it, the rail (md) or the phone sheet.
  const laptop = new MediaQuery('min-width: 1024px');
</script>

<!-- Capture, so a G chord sees its second key before a table's J / K handler can claim it. -->
<svelte:window onkeydowncapture={onkeydown} />

<!-- The full sidebar from lg, the icon rail at md, and a sheet behind the menu button on phones (design system section 8). -->
<Sidebar.Provider open={laptop.current}>
  <AppSidebar user={data.user} {viewCounts} slack={slack.data?.data} />
  <Sidebar.Inset class="min-w-0">
    {@render children()}
  </Sidebar.Inset>
</Sidebar.Provider>

<NotificationTray items={notifications.items} ondismiss={(id) => notifications.dismiss(id)} onhold={(reason, held) => notifications.hold(reason, held)} />
<!-- A save re-reads the settings, so Close automatically applies to the toasts already showing, not only the next. -->
<NotificationSettingsSheet
  bind:open={notificationsOpen}
  onsaved={(sentence) => {
    announcement = sentence;
    void readSettings();
  }}
/>
<p role="status" class="sr-only">{announcement}</p>
<CommandPalette bind:open={paletteOpen} />
<ShortcutsDialog bind:open={shortcutsOpen} />
