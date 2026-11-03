<script lang="ts">
  import CalendarIcon from '~icons/lucide/calendar';
  import SendIcon from '~icons/lucide/send';
  import type { HttpValidationProblemDetails, Interviewer } from '@screenhire/api-client';
  import { ApiError, createListInterviewers, createScheduleInterview } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, setError, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import FailureText from '#lib/components/failure-text.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { checkboxClass, selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';
  import { zoneName } from '#lib/report-range.js';
  import {
    defaultDayEnd,
    defaultDays,
    defaultDayStart,
    durationLabel,
    durations,
    maxInterviewers,
    scheduleSchema,
    timeOptions,
    timeZoneOptions,
    toggleInterviewer,
    windowInstants,
  } from '#lib/schedule-interview.js';
  import { getShell } from '#lib/shell.js';
  import { cn } from '#lib/utils.js';

  // Schedule interview (docs/architecture/interview-scheduling.md), in a sheet (design system section 9): who interviews,
  // how long, which days the candidate may choose from and the working hours inside them. It emails the candidate a link to
  // pick a time; nothing is on a calendar until they do. The people are the members who see the job (ADR-0015), and one
  // without a connected calendar is listed but cannot be ticked, because their free time could not be read. The first one
  // ticked is the organizer: their calendar sends the invitation and makes the meeting link.
  let {
    open = $bindable(false),
    applicationId,
    jobId,
    candidateName,
    currentUserId,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    jobId: string;
    candidateName: string;
    currentUserId: string | undefined;
    /** After the interview was offered: a sentence for the page's status line. */
    ondone?: (message: string) => void;
  } = $props();

  const shell = getShell();
  const queryClient = useQueryClient();
  // Only while the sheet is open: listing interviewers takes the permission that emails candidates.
  const interviewers = createListInterviewers(
    () => ({ jobId }),
    () => ({ query: { enabled: open } }),
  );
  const people = $derived<readonly Interviewer[] | undefined>(interviewers.data?.data);
  const nobodyConnected = $derived(people !== undefined && !people.some((person) => person.calendarConnected));
  const nameOf = (userId: string) => people?.find((person) => person.userId === userId)?.displayName ?? 'A teammate';

  const schedule = createScheduleInterview(() => ({
    mutation: { onSettled: () => invalidatePath(queryClient, `${applicationsPath}/${applicationId}`) },
  }));

  // The browser's own zone and tomorrow to two weeks ahead, until the person says otherwise.
  const empty = () => {
    const timeZone = zoneName();
    return { interviewerUserIds: [] as string[], durationMinutes: 30, ...defaultDays(Date.now(), timeZone), timeZone, dayStart: defaultDayStart, dayEnd: defaultDayEnd };
  };
  const zones = $derived(timeZoneOptions(zoneName()));

  const { form, errors, enhance, submitting, reset } = superForm(defaults(empty(), zod4(scheduleSchema)), {
    SPA: true,
    validators: zod4(scheduleSchema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const value = submitted.data;
      const instants = windowInstants(value.windowStart, value.windowEnd, value.timeZone);
      if (!instants) return;
      try {
        await schedule.mutateAsync({
          applicationId,
          data: {
            interviewerUserIds: value.interviewerUserIds,
            durationMinutes: value.durationMinutes,
            ...instants,
            timeZone: value.timeZone,
            dayStart: value.dayStart,
            dayEnd: value.dayEnd,
          },
        });
        open = false;
        ondone?.(`Offered ${candidateName} times to choose from.`);
      } catch (error) {
        submitted.valid = false;
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else if (error instanceof ApiError && error.status === 422) {
          // Someone chosen has no working calendar: the API names them by id, and the words are ours.
          const names = ((error.body as { interviewerUserIds?: string[] } | undefined)?.interviewerUserIds ?? []).map(nameOf);
          const who = names.length === 0 ? 'Someone you chose' : names.length === 1 ? names[0]! : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;
          setError(submitted, 'interviewerUserIds._errors', `${who} ${names.length > 1 ? 'have' : 'has'} no working calendar connected. Ask them to connect one from the account menu.`);
        } else if (error instanceof ApiError && error.status === 404) {
          submitted.errors._errors = ['Scheduling is not set up here, so the candidate could not be sent a link.'];
        } else {
          // 409 carries its own words: an interview already open or booked ("Cancel it first"), or an application that ended.
          submitted.errors._errors = [describeError(error, 'Could not offer the interview')];
        }
      }
    },
  });

  // Starts fresh each time it opens.
  let preselected = false;
  $effect(() => {
    if (!open) return;
    untrack(() => {
      reset({ data: empty() });
      preselected = false;
    });
  });

  // The person scheduling is the likely interviewer, so they start ticked when their calendar is connected.
  $effect(() => {
    const everyone = people;
    if (!open || preselected || !everyone) return;
    preselected = true;
    untrack(() => {
      if (currentUserId && everyone.some((person) => person.userId === currentUserId && person.calendarConnected)) $form.interviewerUserIds = [currentUserId];
    });
  });

  const organizer = $derived($form.interviewerUserIds[0] ? nameOf($form.interviewerUserIds[0]) : undefined);
  const describedBy = (id: string, messages: string[] | undefined) => (messages ? id : undefined);

  function connectCalendar() {
    open = false;
    shell?.openCalendar();
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header>
      <Sheet.Title>Schedule interview</Sheet.Title>
      <Sheet.Description>{candidateName} gets a link to pick a time.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-5 px-4 pb-4" data-schedule-interview>
      {#if interviewers.isError}
        <QueryError message={describeError(interviewers.error, 'Could not load the interviewers')} onretry={() => interviewers.refetch()} inset />
      {:else if !people}
        <div aria-hidden="true" class="flex flex-col gap-2">
          {#each [0, 1, 2] as row (row)}<Skeleton class="h-6 w-56" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the interviewers…</p>
      {:else if nobodyConnected}
        <!-- Nobody can be offered until a calendar is connected, and this button opens the sheet that does it. -->
        <Empty.Root>
          <Empty.Header>
            <Empty.Description>Nobody who sees this job has a calendar connected.</Empty.Description>
          </Empty.Header>
          <Empty.Content><Button type="button" variant="outline" size="lg" onclick={connectCalendar}><CalendarIcon aria-hidden="true" />Connect your calendar</Button></Empty.Content>
        </Empty.Root>
      {:else}
        <fieldset
          class="flex min-w-0 flex-col gap-2"
          aria-describedby={$errors.interviewerUserIds?._errors ? 'schedule-interviewers-error' : undefined}
        >
          <legend class="mb-1 text-body font-medium">Interviewers</legend>
          {#each people as person (person.userId)}
            {@const noteId = `schedule-interviewer-${person.userId}-note`}
            <label class="flex min-w-0 items-start gap-2 py-0.5 pointer-coarse:min-h-11 pointer-coarse:items-center">
              <input
                type="checkbox"
                name="interviewerUserIds"
                value={person.userId}
                class={cn(checkboxClass, 'mt-0.5 pointer-coarse:mt-0')}
                disabled={!person.calendarConnected}
                checked={$form.interviewerUserIds.includes(person.userId)}
                aria-describedby={person.calendarConnected ? undefined : noteId}
                onchange={(event) => ($form.interviewerUserIds = toggleInterviewer($form.interviewerUserIds, person.userId, event.currentTarget.checked))}
              />
              <span class="flex min-w-0 flex-col">
                <span class={cn('break-words', !person.calendarConnected && 'text-muted-foreground')}>{person.displayName}</span>
                {#if !person.calendarConnected}<span id={noteId} class="text-muted-foreground">No calendar connected</span>{/if}
              </span>
            </label>
          {/each}
          {#if organizer}<p class="min-w-0 break-words text-muted-foreground">{organizer}'s calendar sends the invitation.</p>{/if}
          <Field.Error id="schedule-interviewers-error" errors={$errors.interviewerUserIds?._errors?.map((message) => ({ message }))} />
          {#if $form.interviewerUserIds.length >= maxInterviewers}<p class="text-muted-foreground">That is the most one interview takes.</p>{/if}
        </fieldset>

        <Field.Field data-invalid={$errors.durationMinutes ? true : undefined}>
          <Field.Label for="schedule-length">Length</Field.Label>
          <select
            id="schedule-length"
            name="durationMinutes"
            class={selectClass}
            bind:value={$form.durationMinutes}
            aria-invalid={$errors.durationMinutes ? 'true' : undefined}
            aria-describedby={describedBy('schedule-length-error', $errors.durationMinutes)}
          >
            {#each durations as minutes (minutes)}<option value={minutes}>{durationLabel(minutes)}</option>{/each}
          </select>
          <Field.Error id="schedule-length-error" errors={$errors.durationMinutes?.map((message) => ({ message }))} />
        </Field.Field>

        <div class="grid gap-5 sm:grid-cols-2">
          <Field.Field data-invalid={$errors.windowStart ? true : undefined}>
            <Field.Label for="schedule-first-day">First day</Field.Label>
            <Input
              id="schedule-first-day"
              name="windowStart"
              type="date"
              bind:value={$form.windowStart}
              aria-invalid={$errors.windowStart ? 'true' : undefined}
              aria-describedby={describedBy('schedule-first-day-error', $errors.windowStart)}
            />
            <Field.Error id="schedule-first-day-error" errors={$errors.windowStart?.map((message) => ({ message }))} />
          </Field.Field>
          <Field.Field data-invalid={$errors.windowEnd ? true : undefined}>
            <Field.Label for="schedule-last-day">Last day</Field.Label>
            <Input
              id="schedule-last-day"
              name="windowEnd"
              type="date"
              bind:value={$form.windowEnd}
              aria-invalid={$errors.windowEnd ? 'true' : undefined}
              aria-describedby={describedBy('schedule-last-day-error', $errors.windowEnd)}
            />
            <Field.Error id="schedule-last-day-error" errors={$errors.windowEnd?.map((message) => ({ message }))} />
          </Field.Field>
        </div>

        <Field.Field data-invalid={$errors.timeZone ? true : undefined}>
          <Field.Label for="schedule-zone">Time zone</Field.Label>
          <select
            id="schedule-zone"
            name="timeZone"
            class={selectClass}
            bind:value={$form.timeZone}
            aria-invalid={$errors.timeZone ? 'true' : undefined}
            aria-describedby={describedBy('schedule-zone-error', $errors.timeZone)}
          >
            {#each zones as zone (zone.value)}<option value={zone.value}>{zone.label}</option>{/each}
          </select>
          <Field.Error id="schedule-zone-error" errors={$errors.timeZone?.map((message) => ({ message }))} />
        </Field.Field>

        <fieldset class="flex min-w-0 flex-col gap-2">
          <legend class="mb-1 text-body font-medium">Working hours</legend>
          <div class="grid gap-5 sm:grid-cols-2">
            <Field.Field data-invalid={$errors.dayStart ? true : undefined}>
              <Field.Label for="schedule-day-start">From</Field.Label>
              <select
                id="schedule-day-start"
                name="dayStart"
                class={selectClass}
                bind:value={$form.dayStart}
                aria-invalid={$errors.dayStart ? 'true' : undefined}
                aria-describedby={describedBy('schedule-day-start-error', $errors.dayStart)}
              >
                {#each timeOptions as time (time.value)}<option value={time.value}>{time.label}</option>{/each}
              </select>
              <Field.Error id="schedule-day-start-error" errors={$errors.dayStart?.map((message) => ({ message }))} />
            </Field.Field>
            <Field.Field data-invalid={$errors.dayEnd ? true : undefined}>
              <Field.Label for="schedule-day-end">To</Field.Label>
              <select
                id="schedule-day-end"
                name="dayEnd"
                class={selectClass}
                bind:value={$form.dayEnd}
                aria-invalid={$errors.dayEnd ? 'true' : undefined}
                aria-describedby={describedBy('schedule-day-end-error', $errors.dayEnd)}
              >
                {#each timeOptions as time (time.value)}<option value={time.value}>{time.label}</option>{/each}
              </select>
              <Field.Error id="schedule-day-end-error" errors={$errors.dayEnd?.map((message) => ({ message }))} />
            </Field.Field>
          </div>
          <p class="text-muted-foreground">Weekdays only.</p>
        </fieldset>
      {/if}
      <FailureText message={$errors._errors?.[0]} />
      {#if !nobodyConnected}
        <div class="flex justify-end gap-2">
          <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
          {#if people}
            <Button type="submit" size="lg" disabled={$submitting}><SendIcon aria-hidden="true" />Send invitation</Button>
          {/if}
        </div>
      {/if}
    </form>
  </Sheet.Content>
</Sheet.Root>
