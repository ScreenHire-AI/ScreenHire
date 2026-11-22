<script lang="ts">
  import { createGetCalendar, createGetSlack } from '@screenhire/api-client/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { accountCrumbs } from '#lib/account.js';
  import { calendarFailure, isCalendarOutcome } from '#lib/calendar.js';
  import AccountMenu from '#lib/components/account-menu.svelte';
  import CalendarConnection from '#lib/components/calendar-connection.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import InterviewHoursPanels from '#lib/components/interview-hours-panels.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SlackConnection from '#lib/components/slack-connection.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import { isSlackOutcome, slackFailure } from '#lib/slack.js';

  // Your account > Calendar and accounts (design system section 21): the person's own interview hours and ScreenHire's calendar
  // with its subscribe link, always (docs/architecture/interview-scheduling.md), then their calendar and Slack account
  // (docs/architecture/slack.md), each a section, shown only where this ScreenHire has one to connect (A3).
  // Connecting goes through the provider's page and comes back here with ?calendar= or ?slack-account=, which says how it went
  // in that section and leaves the address so a reload does not repeat it.
  const calendar = createGetCalendar();
  const slack = createGetSlack();
  const calendarStatus = $derived(calendar.data?.data);
  const slackStatus = $derived(slack.data?.data);
  const calendarShown = $derived(Boolean(calendarStatus && calendarStatus.providers.length > 0));
  const slackShown = $derived(Boolean(slackStatus?.available && slackStatus.workspace));

  // Read once, from the address the round trip came back to.
  const calendarOutcome = isCalendarOutcome(page.url.searchParams.get('calendar')) ? (calendarFailure(page.url.searchParams.get('calendar')) ?? '') : '';
  const slackOutcome = isSlackOutcome(page.url.searchParams.get('slack-account')) ? (slackFailure(page.url.searchParams.get('slack-account')) ?? '') : '';
  $effect(() => {
    if (!page.url.searchParams.has('calendar') && !page.url.searchParams.has('slack-account')) return;
    const url = new URL(page.url.href);
    url.searchParams.delete('calendar');
    url.searchParams.delete('slack-account');
    void goto(url, { replace: true, reset: false });
  });
</script>

<svelte:head>
  <title>Calendar and accounts · ScreenHire</title>
</svelte:head>

<TopBar crumbs={accountCrumbs('/account/connected')} />

<main class="flex flex-col">
  <HeaderBand title="Calendar and accounts" oneRow />

  <AccountMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      <InterviewHoursPanels />
      {#if calendar.isError && slack.isError}
        <Panel title="Calendar">
          <QueryError message={describeError(calendar.error, 'Could not load your connected accounts')} onretry={() => (calendar.refetch(), slack.refetch())} inset />
        </Panel>
      {:else if calendar.isPending || slack.isPending}
        <Panel title="Calendar">
          <div aria-hidden="true"><Skeleton class="h-20 w-full" /></div>
          <p class="sr-only" role="status">Loading your connected accounts…</p>
        </Panel>
      {:else}
        {#if calendarShown && calendarStatus}
          <Panel title="Calendar"><CalendarConnection status={calendarStatus} outcome={calendarOutcome} /></Panel>
        {/if}
        {#if slackShown && slackStatus}
          <Panel title="Slack">
            {#if slackStatus.workspace}<p class="mb-3 text-muted-foreground">{slackStatus.workspace.teamName}</p>{/if}
            <SlackConnection status={slackStatus} outcome={slackOutcome} />
          </Panel>
        {/if}
      {/if}
    </div>
  </AccountMenu>
</main>
