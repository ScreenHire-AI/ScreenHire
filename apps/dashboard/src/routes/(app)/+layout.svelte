<script lang="ts">
  import { getApplication, listAutomationRuns, listTeamMembers } from '@screenhire/api-client';
  import {
    createFillTimeZone,
    createGetAccount,
    createGetBilling,
    createGetCalendar,
    createGetOrganizationSendingDomain,
    createGetSlack,
    createListJobs,
    createListTasks,
    createListViews,
    createUpdateAccountPreferences,
    getGetAccountNotificationsQueryOptions,
    getGetApplicationQueryOptions,
    getGetEmailTemplateQueryOptions,
    getGetHiringTemplateQueryOptions,
    getGetScorecardTemplateQueryOptions,
    getListApplicationTasksQueryOptions,
    getListMemberJobsQueryOptions,
    getListTeamMembersQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { setQueryClientContext } from '@tanstack/svelte-query';
  import { onDestroy, untrack } from 'svelte';
  import { MediaQuery } from 'svelte/reactivity';
  import { goto } from '$app/navigation';
  import AppSidebar from '#lib/components/app-sidebar.svelte';
  import CommandPalette from '#lib/components/command-palette.svelte';
  import NotificationTray from '#lib/components/notification-tray.svelte';
  import UndoNotice from '#lib/components/undo-notice.svelte';
  import { setUndo } from '#lib/undo-context.js';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import {
    dataRequestNotification,
    mentionNotification,
    mentionNotificationId,
    Notifications,
    replyNotification,
    teamNotification,
  } from '#lib/notifications.svelte.js';
  import { createQueryClient } from '#lib/query.js';
  import { startLiveUpdates } from '#lib/realtime.js';
  import { sidebarViews } from '#lib/sidebar-views.js';
  import { can } from '#lib/permissions.js';
  import { sendingDomainMark } from '#lib/sending-domain.js';
  import { trialEnd } from '#lib/billing.js';
  import { clock } from '#lib/clock.svelte.js';
  import { billingFeature, slackFeature } from '#lib/settings.js';
  import { sectionsFor, setShell } from '#lib/shell.js';
  import { createChordHandler } from '#lib/shortcuts.js';
  import { configureDates } from '#lib/date-settings.svelte.js';
  import { cardShows, defaultNotificationSettings, toastSettingsFrom, type ToastSettings } from '#lib/teammate-changes.js';
  import { TeammateToasts } from '#lib/teammate-toasts.js';
  import { UndoOffer } from '#lib/undo.svelte.js';
  import { createViewActions } from '#lib/views.svelte.js';
  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps = $props();

  // One query cache and one hub connection for the signed-in shell. Leaving it (signing out) drops both, so nothing
  // cached for this account outlives the session.
  const queryClient = createQueryClient();
  setQueryClientContext(queryClient);
  const live = startLiveUpdates(queryClient);

  // The person's toast choices from the notification grid on Your account, kept fresh in the cache: the page invalidates
  // them when it saves, so the next toast follows the change.
  let closeToasts = true;
  const notifications = new Notifications({ closeToasts: () => closeToasts });
  async function readSettings(): Promise<ToastSettings> {
    try {
      const settings = toastSettingsFrom((await queryClient.fetchQuery(getGetAccountNotificationsQueryOptions())).data);
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
      templateName: async (templateType, templateId) => {
        switch (templateType) {
          case 'EmailTemplate':
            return (await queryClient.fetchQuery(getGetEmailTemplateQueryOptions(templateId))).data.name;
          case 'ScorecardTemplate':
            return (await queryClient.fetchQuery(getGetScorecardTemplateQueryOptions(templateId))).data.name;
          default:
            return (await queryClient.fetchQuery(getGetHiringTemplateQueryOptions(templateId))).data.name;
        }
      },
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
    // A candidate's data request shows a card to the people who may act on it (Erase candidates, every job); it carries no name.
    if (event.type === 'DataRequestReceived') {
      const card = dataRequestNotification(data.user, event.data.requestId, event.data.kind);
      if (card) notifications.add(card);
    }
    // A candidate's reply shows a card to the person who sent the email it answers, or, when an automation sent it, to everyone
    // the event reaches (the members who see the job). An automatic answer (out of office) shows none.
    if (event.type === 'CandidateReplied' && !event.data.autoReply && (!event.data.senderUserId || event.data.senderUserId === data.user?.id)) {
      void notifyReply(event.data.applicationId, event.data.inboundEmailId, Boolean(event.data.senderUserId));
    }
  });

  async function notifyReply(applicationId: string, inboundEmailId: string, answersYou: boolean) {
    try {
      // In the app off for Candidate replies (or toasts paused) shows no card; the bell keeps the reply either way.
      if (!cardShows(await readSettings(), 'Replies')) return;
      const application = await getApplication(applicationId);
      if (application.status !== 200) return;
      notifications.add(replyNotification(inboundEmailId, application.data, answersYou));
    } catch {
      // As for NotifyTeam: the reply stays in Messages, and the email went out either way.
    }
  }

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
      if (!cardShows(await readSettings(), 'Mentions')) return;
      const [application, team] = await Promise.all([getApplication(applicationId), listTeamMembers()]);
      if (application.status !== 200) return;
      const author = team.status === 200 ? team.data.find((member) => member.userId === authorUserId)?.displayName : undefined;
      notifications.add(mentionNotification(noteId, application.data, author));
    } catch {
      // As for NotifyTeam: the note stays on the application's page, and the email went out either way.
    }
  }

  onDestroy(() => {
    preferences.saveHabitsWith(undefined);
    stopNotifying();
    stopToasting();
    teammateToasts.dispose();
    notifications.dispose();
    void live.stop();
    queryClient.clear();
  });

  let paletteOpen = $state(false);
  let shortcutsOpen = $state(false);
  // The Calendar sheet is the shell's (AppSidebar mounts it); a page that needs a calendar opens it through the shell, and
  // Your account > Connected accounts shows the same connection as a section.
  let calendarOpen = $state(false);
  setShell({
    openPalette: () => (paletteOpen = true),
    openShortcuts: () => (shortcutsOpen = true),
    openNotifications: () => void goto('/account/notifications'),
    openCalendar: () => (calendarOpen = true),
    hasConnections: () => Boolean((calendar.data?.data.providers.length ?? 0) > 0 || (slack.data?.data.available && slack.data.data.workspace)),
    refreshAccount: () => void account.refetch(),
    live: () => live.connection.status,
    user: () => data.user,
    // Missing until the answer is in, and for anyone who may not ask: the Settings menu marks the domain once it works or needs fixing.
    sendingDomainMark: () => (sendingDomain.data ? sendingDomainMark(sendingDomain.data.data.domain) : undefined),
    toast: (toast) => notifications.toast(toast),
    features: () => ({
      slack: slackFeature(slack.data?.data, slack.isError),
      // A failed query says nothing about the feature, so the entry stays and the page reports the failure.
      sendingDomain: sendingDomain.data?.data.available,
      billing: billingFeature(billing.data?.data, billing.isError),
    }),
  });

  // A chord reads the sections this person may go to, so G V does nothing for a role without Verification.
  const chords = $derived(createChordHandler(Object.fromEntries(sectionsFor(data.user).map((section) => [section.key, section.href])), (href) => void goto(href)));

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

  // The Views list: the built-in views and the saved ones, with no counts (design system I2, T4). A deleted view's Undo is
  // offered in the notice below the page.
  const savedViewList = createListViews();
  const jobList = createListJobs();
  const undo = new UndoOffer();
  setUndo(undo);
  const viewActions = createViewActions({ undo, hiddenBuiltIns: () => savedViewList.data?.data.hiddenBuiltIns ?? [] });
  const jobTitles = $derived(Object.fromEntries((jobList.data?.data ?? []).map((job) => [job.id, job.title])));
  const views = $derived(
    sidebarViews({
      saved: savedViewList.data?.data.views ?? [],
      hiddenBuiltIns: savedViewList.data?.data.hiddenBuiltIns ?? [],
      sidebarOrder: savedViewList.data?.data.sidebarOrder ?? [],
      jobTitles,
    }),
  );
  // What the sheet of team views offers: sharing is an offer, and a person adds the views they want to their sidebar.
  const teamViews = $derived(
    savedViewList.data ? { views: savedViewList.data.data.views, seenAt: savedViewList.data.data.teamViewsSeenAt, jobTitles } : undefined,
  );

  // The person's own open tasks, for the count after My tasks. Live updates invalidate it with the tasks page's lists.
  const myTasks = createListTasks(() => ({ status: 'Open', scope: 'Mine' }), () => ({ query: { enabled: Boolean(data.user?.organization), staleTime: 60_000 } }));
  const openTasks = $derived(myTasks.data?.data.length);

  // Slack, for the Slack account item in each person's menu (docs/architecture/slack.md); it changes rarely.
  const slack = createGetSlack(() => ({ query: { enabled: Boolean(data.user?.organization), staleTime: 60_000 } }));
  // Whether this installation can send from a company's own domain, for Settings' entry (A3). Only a person who edits settings
  // may ask; anyone else never sees the entry, so the API's refusal is no answer about the feature.
  const sendingDomain = createGetOrganizationSendingDomain(() => ({
    query: { enabled: can(data.user, 'EditSettings'), staleTime: 60_000 },
  }));
  // Whether a payment provider is set up, for Settings' Billing entry (A3). Only an Owner may ask, and anyone else never sees the entry.
  const billing = createGetBilling(() => ({ query: { enabled: can(data.user, 'ManageBilling'), staleTime: 60_000 } }));
  // Only an Owner asks for billing, so only an Owner is told when the trial ends; the minute clock drops the line when it has.
  const trialEndsAt = $derived(trialEnd(billing.data?.data, clock.now));
  // The person's calendar, for the Calendar item in their menu (docs/architecture/interview-scheduling.md); it changes rarely.
  const calendar = createGetCalendar(() => ({ query: { enabled: Boolean(data.user?.organization), staleTime: 60_000 } }));

  // The person's own account here (Your account): their dates, and their two habits kept on the server. A habit never saved
  // there takes this browser's earlier choice, saved once; a time zone never set takes the browser's, so the daily digest
  // comes at 08:00 their time (docs/architecture/notifications.md).
  const account = createGetAccount(() => ({ query: { enabled: Boolean(data.user?.organization), staleTime: 60_000 } }));
  const saveHabits = createUpdateAccountPreferences();
  const fillTimeZone = createFillTimeZone();
  preferences.saveHabitsWith((habits) => saveHabits.mutate({ data: habits }));
  $effect(() => {
    const details = account.data?.data;
    if (!details) return;
    untrack(() => {
      configureDates({ language: details.language, timeZone: details.timeZone, dateFormat: details.dateFormat });
      preferences.applyHabits(details);
      if (details.shortcutsEnabled == null || details.confirmMoves == null) saveHabits.mutate({ data: preferences.habits });
      if (!details.timeZone) {
        const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
        if (zone) fillTimeZone.mutate({ data: { timeZone: zone } });
      }
    });
  });

  // Tailwind's lg: from there up the sidebar is full; below it, the rail (md) or the phone sheet.
  const laptop = new MediaQuery('min-width: 1024px');
</script>

<!-- Capture, so a G chord sees its second key before a table's J / K handler can claim it. -->
<svelte:window onkeydowncapture={onkeydown} />

<!-- The full sidebar from lg, the icon rail at md, and a sheet behind the menu button on phones (design system section 8). -->
<Sidebar.Provider open={laptop.current}>
  <AppSidebar user={data.user} {views} {viewActions} {teamViews} calendar={calendar.data?.data} {openTasks} {trialEndsAt} bind:calendarOpen />
  <Sidebar.Inset class="min-w-0">
    {@render children()}
  </Sidebar.Inset>
</Sidebar.Provider>

<UndoNotice offer={undo} />
<NotificationTray items={notifications.items} ondismiss={(id) => notifications.dismiss(id)} onhold={(reason, held) => notifications.hold(reason, held)} />
<CommandPalette bind:open={paletteOpen} />
<ShortcutsDialog bind:open={shortcutsOpen} user={data.user} />
