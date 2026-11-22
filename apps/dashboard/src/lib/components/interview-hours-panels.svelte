<script lang="ts">
  import CopyIcon from '~icons/lucide/copy';
  import RefreshCwIcon from '~icons/lucide/refresh-cw';
  import type { DayHours, InterviewHoursSettings } from '@screenhire/api-client';
  import {
    ApiError,
    createGetInterviewHours,
    createTurnOffCalendarFeed,
    createTurnOnCalendarFeed,
    createUpdateInterviewHours,
    getGetInterviewHoursQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import WeeklyHoursEditor from '#lib/components/weekly-hours-editor.svelte';
  import { checkboxClass, selectClass } from '#lib/components/workflow/controls.js';
  import { hoursProblem, weekdayHours, weekly } from '#lib/interview-options.js';
  import { describeError } from '#lib/query.js';
  import { timeZoneOptions } from '#lib/schedule-interview.js';

  // A person's own interview hours and ScreenHire's calendar (docs/architecture/interview-scheduling.md, "Personal hours and
  // ScreenHire's calendar"), on Your account, Calendar: the days and hours they interview, the time kept free around each
  // interview, the most in a day, and the zone. Every way of scheduling respects them, and with hours set someone with no
  // Google or Outlook calendar can be scheduled. The subscribe link puts their interviews in any calendar app.
  const queryClient = useQueryClient();
  const settings = createGetInterviewHours();
  const save = createUpdateInterviewHours();
  const turnOn = createTurnOnCalendarFeed();
  const turnOff = createTurnOffCalendarFeed();

  const bufferChoices = [0, 5, 10, 15, 30, 45, 60];
  const perDayChoices = [1, 2, 3, 4, 5, 6, 8, 10];

  // The draft, filled once from the server and saved whole.
  let draft = $state<{ timeZone: string; hasHours: boolean; days: DayHours[]; bufferMinutes: number; maxPerDay: number | null } | undefined>();
  $effect(() => {
    const loaded = settings.data?.data;
    if (!loaded || draft) return;
    draft = {
      timeZone: loaded.timeZone,
      hasHours: loaded.weekly != null,
      days: loaded.weekly?.days ?? weekdayHours(),
      bufferMinutes: loaded.bufferMinutes,
      maxPerDay: loaded.maxPerDay ?? null,
    };
  });

  const zones = $derived(timeZoneOptions(draft?.timeZone));
  const feedUrl = $derived(settings.data?.data.feedUrl ?? null);
  let failure = $state('');
  let saved = $state('');
  let feedFailure = $state('');
  let copied = $state('');
  let resetOpen = $state(false);
  const hoursError = $derived(draft?.hasHours ? hoursProblem(draft.days, 15) : undefined);

  const stored = (value: InterviewHoursSettings) => queryClient.setQueryData(getGetInterviewHoursQueryKey(), { data: value, status: 200, headers: new Headers() });

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    if (!draft || hoursError) return;
    failure = '';
    saved = '';
    try {
      const response = await save.mutateAsync({
        data: { timeZone: draft.timeZone, weekly: draft.hasHours ? weekly(draft.days) : null, bufferMinutes: draft.bufferMinutes, maxPerDay: draft.maxPerDay },
      });
      stored(response.data);
      saved = 'Saved your interview hours.';
    } catch (error) {
      failure = error instanceof ApiError && error.status === 400 ? describeError(error, 'Check your hours') : describeError(error, 'Could not save your hours');
    }
  }

  async function feed(on: boolean) {
    feedFailure = '';
    copied = '';
    try {
      const response = on ? await turnOn.mutateAsync() : await turnOff.mutateAsync();
      stored(response.data);
    } catch (error) {
      feedFailure = describeError(error, on ? 'Could not make the link' : 'Could not turn the link off');
    }
  }

  async function copy() {
    if (!feedUrl) return;
    try {
      await navigator.clipboard.writeText(feedUrl);
      copied = 'Copied the link.';
    } catch {
      copied = 'Select the link and copy it.';
    }
  }
</script>

<Panel title="Interview hours">
  {#if settings.isError}
    <QueryError message={describeError(settings.error, 'Could not load your interview hours')} onretry={() => settings.refetch()} inset />
  {:else if !draft}
    <div aria-hidden="true"><Skeleton class="h-24 w-full" /></div>
    <p class="sr-only" role="status">Loading your interview hours…</p>
  {:else}
    <form class="flex flex-col gap-5" onsubmit={submit} data-interview-hours>
      <p class="text-muted-foreground">When you interview. Scheduling offers only these times, and keeps your calendar's busy time and the limits below.</p>
      <label class="flex items-center gap-2 pointer-coarse:min-h-11">
        <input type="checkbox" class={checkboxClass} bind:checked={draft.hasHours} />
        Set my interview hours
      </label>
      {#if draft.hasHours}
        <WeeklyHoursEditor bind:value={draft.days} id="account-hours" legend="Days and hours" errors={hoursError ? [hoursError] : undefined} />
      {/if}
      <div class="grid gap-5 sm:grid-cols-2">
        <Field.Field>
          <Field.Label for="account-buffer">Time between interviews</Field.Label>
          <select id="account-buffer" class={selectClass} bind:value={draft.bufferMinutes}>
            {#each bufferChoices as minutes (minutes)}<option value={minutes}>{minutes === 0 ? 'None' : `${minutes} minutes`}</option>{/each}
          </select>
        </Field.Field>
        <Field.Field>
          <Field.Label for="account-per-day">Most interviews in a day</Field.Label>
          <select
            id="account-per-day"
            class={selectClass}
            value={draft.maxPerDay === null ? '' : String(draft.maxPerDay)}
            onchange={(event) => draft && (draft.maxPerDay = event.currentTarget.value ? Number(event.currentTarget.value) : null)}
          >
            <option value="">No limit</option>
            {#each perDayChoices as count (count)}<option value={String(count)}>{count}</option>{/each}
          </select>
        </Field.Field>
      </div>
      <Field.Field>
        <Field.Label for="account-hours-zone">Time zone</Field.Label>
        <select id="account-hours-zone" class={selectClass} bind:value={draft.timeZone}>
          {#each zones as zone (zone.value)}<option value={zone.value}>{zone.label}</option>{/each}
        </select>
      </Field.Field>
      <FailureText message={failure} />
      <p role="status" class="text-muted-foreground">{saved}</p>
      <Button type="submit" size="lg" class="w-fit" disabled={save.isPending}>Save hours</Button>
    </form>
  {/if}
</Panel>

<Panel title="ScreenHire calendar">
  <div class="flex flex-col gap-3" data-calendar-feed>
    <p class="text-muted-foreground">
      Your interviews booked through ScreenHire, in any calendar app that subscribes to a link, such as the one on your phone. The link shows the times and jobs,
      never a candidate's name.
    </p>
    {#if feedUrl}
      <Field.Field>
        <Field.Label for="account-feed-url">Your private link</Field.Label>
        <Input id="account-feed-url" value={feedUrl} readonly onfocus={(event) => event.currentTarget.select()} />
      </Field.Field>
      <div class="flex flex-wrap gap-2">
        <Button type="button" variant="outline" onclick={copy}><CopyIcon aria-hidden="true" />Copy link</Button>
        <Button type="button" variant="outline" onclick={() => (resetOpen = true)}><RefreshCwIcon aria-hidden="true" />Reset link</Button>
        <Button type="button" variant="outline" disabled={turnOff.isPending} onclick={() => feed(false)}>Turn off</Button>
      </div>
      <p role="status" class="text-muted-foreground">{copied}</p>
    {:else if settings.data}
      <Button type="button" variant="outline" class="w-fit" disabled={turnOn.isPending} onclick={() => feed(true)}>Make my link</Button>
    {/if}
    <FailureText message={feedFailure} />
  </div>
</Panel>

<ConfirmDialog
  bind:open={resetOpen}
  title="Reset your link?"
  description="The old link stops working, so any calendar that subscribed to it stops updating. Subscribe again with the new one."
  cancelLabel="Keep it"
  confirmLabel="Reset link"
  failure="Could not reset the link"
  onconfirm={async () => {
    const response = await turnOn.mutateAsync();
    stored(response.data);
  }}
>
  {#snippet icon()}<RefreshCwIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>
