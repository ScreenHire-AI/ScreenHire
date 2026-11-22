<script lang="ts">
  import CalendarIcon from '~icons/lucide/calendar';
  import PlusIcon from '~icons/lucide/plus';
  import SendIcon from '~icons/lucide/send';
  import XIcon from '~icons/lucide/x';
  import type { HttpValidationProblemDetails, Interviewer, OpenTimesRequest, StageScheduling } from '@screenhire/api-client';
  import { ApiError, createListInterviewers, createListTeams, createScheduleInterview } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, setError, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import OpenTimesCheck from '#lib/components/open-times-check.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import WeeklyHoursEditor from '#lib/components/weekly-hours-editor.svelte';
  import WhoIsFree from '#lib/components/who-is-free.svelte';
  import { checkboxClass, selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import {
    canInterview,
    defaultWindow,
    optionOrder,
    optionWords,
    pickedInstants,
    scheduleRequest,
    scheduleSchema,
    stageDefaultPeople,
    weekdayHours,
    weekly,
  } from '#lib/interview-options.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';
  import { zoneName } from '#lib/report-range.js';
  import { durationLabel, durations, maxInterviewers, timeZoneOptions, toggleInterviewer, windowInstants } from '#lib/schedule-interview.js';
  import { getShell } from '#lib/shell.js';
  import { cn } from '#lib/utils.js';
  import { defaultMeetingName } from '#lib/workflow.js';

  // Schedule interview (docs/architecture/interview-scheduling.md), in a sheet (design system section 9). It starts with the
  // decision: how the candidate picks a time (pick from our times, tell us when you're free, or we'll suggest a time), set
  // from the stage's default, so the sheet usually opens ready. Then who interviews, how long, and only what the chosen way
  // needs. Nothing reaches the candidate until Send. The people are the members who see the job (ADR-0015); for times read
  // from calendars, one with no calendar and no hours of their own is listed but cannot be ticked. The first one ticked is
  // the organizer: their calendar sends the invitation and makes the meeting link.
  let {
    open = $bindable(false),
    applicationId,
    jobId,
    candidateName,
    meetingName = defaultMeetingName,
    stageScheduling,
    currentUserId,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    jobId: string;
    candidateName: string;
    /** What the candidate's stage calls the meeting (its `meetingName`), such as "trial shift". */
    meetingName?: string;
    /** The candidate's stage's default: the way to pick, the interviewers, the length, the days ahead and the hours. */
    stageScheduling?: StageScheduling | null;
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
  // A stage's default team is resolved here, so its people stay current as the team changes.
  const teams = createListTeams(() => ({ query: { enabled: open && !!stageScheduling?.interviewerTeamId } }));
  const people = $derived<readonly Interviewer[] | undefined>(interviewers.data?.data);
  const nameOf = (userId: string) => people?.find((person) => person.userId === userId)?.displayName ?? 'A teammate';
  const firstName = $derived(candidateName.split(' ')[0] || candidateName);
  const defaultPeople = $derived(people ? stageDefaultPeople(stageScheduling, teams.data?.data, people) : undefined);

  const schedule = createScheduleInterview(() => ({
    mutation: { onSettled: () => invalidatePath(queryClient, `${applicationsPath}/${applicationId}`) },
  }));

  // The stage's default, else the browser's own zone, half an hour, tomorrow to two weeks ahead and weekdays 9 to 5.
  const empty = () => {
    const timeZone = zoneName();
    const scheduling = stageScheduling;
    return {
      option: scheduling?.option ?? 'OurTimes',
      interviewerUserIds: [] as string[],
      durationMinutes: scheduling?.durationMinutes ?? 30,
      ...defaultWindow(Date.now(), timeZone, scheduling?.windowDays),
      timeZone,
      hours: scheduling?.hours?.days ?? weekdayHours(),
      useBlocks: scheduling?.useBlocks ?? false,
      proposed: ['', ''],
      extra: [] as string[],
    };
  };
  const zones = $derived(timeZoneOptions(zoneName()));

  const { form, errors, enhance, submitting, reset } = superForm(defaults(empty(), zod4(scheduleSchema)), {
    SPA: true,
    validators: zod4(scheduleSchema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    dataType: 'json',
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const request = scheduleRequest(submitted.data, Date.now());
      if (!request) return;
      try {
        await schedule.mutateAsync({ applicationId, data: request });
        open = false;
        ondone?.(
          submitted.data.option === 'TheirTimes'
            ? `Asked ${candidateName} when they are free.`
            : submitted.data.option === 'OurSuggestion'
              ? `Suggested a time to ${candidateName}.`
              : `Offered ${candidateName} times to choose from.`,
        );
      } catch (error) {
        submitted.valid = false;
        if (error instanceof ApiError && error.status === 400) {
          // The list fields keep their errors under `_errors`; the API's names for them differ from the form's.
          const problem = error.body as HttpValidationProblemDetails;
          const lists = { hours: 'hours._errors', proposedTimes: 'proposed._errors', extraTimes: 'extra._errors' } as const;
          for (const [name, path] of Object.entries(lists)) {
            for (const message of problem.errors?.[name] ?? []) setError(submitted, path, message);
          }
          const rest = Object.fromEntries(Object.entries(problem.errors ?? {}).filter(([name]) => !(name in lists)));
          applyValidationProblem(submitted, { ...problem, errors: rest });
        } else if (error instanceof ApiError && error.status === 422) {
          // Someone chosen has neither a calendar nor hours: the API names them by id, and the words are ours.
          const names = ((error.body as { interviewerUserIds?: string[] } | undefined)?.interviewerUserIds ?? []).map(nameOf);
          const who = names.length === 0 ? 'Someone you chose' : names.length === 1 ? names[0]! : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`;
          setError(
            submitted,
            'interviewerUserIds._errors',
            `${who} ${names.length > 1 ? 'have' : 'has'} no working calendar and no interview hours. Ask them to connect a calendar or set their hours in Your account, Calendar.`,
          );
        } else if (error instanceof ApiError && error.status === 404) {
          submitted.errors._errors = ['Scheduling is not set up here, so the candidate could not be sent a link.'];
        } else {
          // 409 carries its own words: an interview already open or booked ("Cancel it first"), or an application that ended.
          submitted.errors._errors = [describeError(error, `Could not offer the ${meetingName}`)];
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

  // The stage's people start ticked; without a default, the person scheduling, when they can interview this way.
  $effect(() => {
    const everyone = people;
    const resolved = defaultPeople;
    const waitingForTeam = !!stageScheduling?.interviewerTeamId && teams.data === undefined && !teams.isError;
    if (!open || preselected || !everyone || !resolved || waitingForTeam) return;
    preselected = true;
    untrack(() => {
      const tickable = (userId: string) => everyone.some((person) => person.userId === userId && canInterview(person, $form.option, $form.useBlocks));
      const fromStage = resolved.selected.filter(tickable);
      if (fromStage.length > 0) $form.interviewerUserIds = fromStage;
      else if (currentUserId && tickable(currentUserId)) $form.interviewerUserIds = [currentUserId];
    });
  });

  // A person who cannot interview this way is unticked when the way changes.
  $effect(() => {
    const option = $form.option;
    const useBlocks = $form.useBlocks;
    const everyone = people;
    if (!everyone) return;
    untrack(() => {
      const kept = $form.interviewerUserIds.filter((userId) => everyone.some((person) => person.userId === userId && canInterview(person, option, useBlocks)));
      if (kept.length !== $form.interviewerUserIds.length) $form.interviewerUserIds = kept;
    });
  });

  const nobodyCanInterview = $derived(people !== undefined && !people.some((person) => canInterview(person, $form.option, $form.useBlocks)));
  const organizer = $derived($form.interviewerUserIds[0] ? nameOf($form.interviewerUserIds[0]) : undefined);
  const describedBy = (id: string, messages: string[] | undefined) => (messages ? id : undefined);

  // What pick from our times would offer, once the sheet holds enough to ask.
  const openTimesRequest = $derived.by((): OpenTimesRequest | undefined => {
    if ($form.option !== 'OurTimes' || $form.interviewerUserIds.length === 0) return undefined;
    const window = windowInstants($form.windowStart, $form.windowEnd, $form.timeZone);
    if (!window || $form.hours.length === 0) return undefined;
    return {
      jobId,
      interviewerUserIds: $form.interviewerUserIds,
      durationMinutes: $form.durationMinutes,
      ...window,
      timeZone: $form.timeZone,
      hours: weekly($form.hours),
      useBlocks: $form.useBlocks,
    };
  });

  // We'll suggest a time: the picked times as instants, for who is free at each.
  const proposedStarts = $derived((pickedInstants($form.proposed, $form.timeZone) ?? []).map((instant) => new Date(instant).toISOString()));

  function connectCalendar() {
    open = false;
    shell?.openCalendar();
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header>
      <Sheet.Title>Schedule {meetingName}</Sheet.Title>
      <Sheet.Description>Nothing reaches {candidateName} until you send it.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-5 px-4 pb-4" data-schedule-interview>
      {#if interviewers.isError}
        <QueryError message={describeError(interviewers.error, 'Could not load the interviewers')} onretry={() => interviewers.refetch()} inset />
      {:else if !people}
        <div aria-hidden="true" class="flex flex-col gap-2">
          {#each [0, 1, 2] as row (row)}<Skeleton class="h-6 w-56" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the interviewers…</p>
      {:else}
        <div class="flex flex-col gap-2">
          <span id="schedule-option-label" class="text-body font-medium">How should {firstName} pick a time?</span>
          <div role="radiogroup" aria-labelledby="schedule-option-label" class="flex flex-col gap-2">
            {#each optionOrder as option (option)}
              <ChoiceCard name="option" value={option} bind:group={$form.option} title={optionWords[option].title} description={optionWords[option].description} />
            {/each}
          </div>
        </div>

        {#if nobodyCanInterview}
          <!-- Times from calendars need someone whose time is known; this opens the sheet that connects a calendar. -->
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>Nobody who sees this job has a calendar connected or interview hours set.</Empty.Description>
            </Empty.Header>
            <Empty.Content>
              <Button type="button" variant="outline" size="lg" onclick={connectCalendar}><CalendarIcon aria-hidden="true" />Connect your calendar or set your hours</Button>
            </Empty.Content>
          </Empty.Root>
        {:else}
          <fieldset
            class="flex min-w-0 flex-col gap-2"
            aria-describedby={$errors.interviewerUserIds?._errors ? 'schedule-interviewers-error' : undefined}
          >
            <legend class="mb-1 text-body font-medium">Interviewers</legend>
            {#each people as person (person.userId)}
              {@const able = canInterview(person, $form.option, $form.useBlocks)}
              {@const noteId = `schedule-interviewer-${person.userId}-note`}
              <label class="flex min-w-0 items-start gap-2 py-0.5 pointer-coarse:min-h-11 pointer-coarse:items-center">
                <input
                  type="checkbox"
                  name="interviewerUserIds"
                  value={person.userId}
                  class={cn(checkboxClass, 'mt-0.5 pointer-coarse:mt-0')}
                  disabled={!able}
                  checked={$form.interviewerUserIds.includes(person.userId)}
                  aria-describedby={able ? undefined : noteId}
                  onchange={(event) => ($form.interviewerUserIds = toggleInterviewer($form.interviewerUserIds, person.userId, event.currentTarget.checked))}
                />
                <span class="flex min-w-0 flex-col">
                  <span class={cn('break-words', !able && 'text-muted-foreground')}>{person.displayName}</span>
                  {#if !able}<span id={noteId} class="text-muted-foreground">No calendar or hours</span>{/if}
                </span>
              </label>
            {/each}
            {#if defaultPeople?.teamMissing}
              <p class="text-muted-foreground">The stage's team no longer exists.</p>
            {:else if defaultPeople && defaultPeople.notOnJob.length > 0}
              <p class="min-w-0 break-words text-muted-foreground">Not on this job's hiring team: {defaultPeople.notOnJob.join(', ')}.</p>
            {/if}
            {#if organizer}<p class="min-w-0 break-words text-muted-foreground">{organizer}'s calendar sends the invitation.</p>{/if}
            <Field.Error id="schedule-interviewers-error" errors={$errors.interviewerUserIds?._errors?.map((message) => ({ message }))} />
            {#if $form.interviewerUserIds.length >= maxInterviewers}<p class="text-muted-foreground">That is the most one interview takes.</p>{/if}
          </fieldset>

          <div class="grid gap-5 sm:grid-cols-2">
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
          </div>

          {#if $form.option === 'OurSuggestion'}
            <fieldset class="flex min-w-0 flex-col gap-3" aria-describedby={$errors.proposed?._errors ? 'schedule-proposed-error' : undefined}>
              <legend class="mb-1 text-body font-medium">Times you suggest</legend>
              <div class="grid gap-5 sm:grid-cols-2">
                <Field.Field>
                  <Field.Label for="schedule-proposed-0">First time</Field.Label>
                  <Input id="schedule-proposed-0" type="datetime-local" bind:value={$form.proposed[0]} />
                </Field.Field>
                <Field.Field>
                  <Field.Label for="schedule-proposed-1">Second time (optional)</Field.Label>
                  <Input id="schedule-proposed-1" type="datetime-local" bind:value={$form.proposed[1]} />
                </Field.Field>
              </div>
              <Field.Error id="schedule-proposed-error" errors={$errors.proposed?._errors?.map((message) => ({ message }))} />
              {#if proposedStarts.length > 0 && $form.interviewerUserIds.length > 0}
                <WhoIsFree
                  {jobId}
                  interviewerUserIds={$form.interviewerUserIds}
                  starts={proposedStarts}
                  durationMinutes={$form.durationMinutes}
                  {nameOf}
                  label="Who is free"
                />
              {/if}
            </fieldset>
          {:else}
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

            {#if $form.option === 'OurTimes'}
              <div class="flex flex-col gap-2">
                <span id="schedule-source-label" class="text-body font-medium">Open times come from</span>
                <div role="radiogroup" aria-labelledby="schedule-source-label" class="grid gap-2 sm:grid-cols-2">
                  <label class="flex items-center gap-2 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring">
                    <input type="radio" name="useBlocks" class="size-4 accent-primary" checked={!$form.useBlocks} onchange={() => ($form.useBlocks = false)} />
                    Their calendars
                  </label>
                  <label class="flex items-center gap-2 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring">
                    <input type="radio" name="useBlocks" class="size-4 accent-primary" checked={$form.useBlocks} onchange={() => ($form.useBlocks = true)} />
                    Interview blocks
                  </label>
                </div>
              </div>
            {/if}

            <WeeklyHoursEditor
              bind:value={$form.hours}
              id="schedule-hours"
              legend={$form.option === 'TheirTimes' ? 'Hours they may pick from' : $form.useBlocks ? 'Interview blocks' : 'Working hours'}
              description={$form.option === 'OurTimes' && $form.useBlocks ? 'Times in these blocks are offered whatever the calendars say.' : undefined}
              errors={$errors.hours?._errors}
            />

            {#if $form.option === 'OurTimes'}
              {#each $form.extra as _, index (index)}
                <div class="flex items-end gap-2">
                  <Field.Field class="min-w-0 flex-1">
                    <Field.Label for={`schedule-extra-${index}`}>One-off time</Field.Label>
                    <Input id={`schedule-extra-${index}`} type="datetime-local" bind:value={$form.extra[index]} />
                  </Field.Field>
                  <Button type="button" variant="ghost" size="icon-lg" onclick={() => ($form.extra = $form.extra.filter((__, at) => at !== index))}>
                    <XIcon aria-hidden="true" /><span class="sr-only">Remove this one-off time</span>
                  </Button>
                </div>
              {/each}
              <Field.Error id="schedule-extra-error" errors={$errors.extra?._errors?.map((message) => ({ message }))} />
              <OpenTimesCheck
                request={openTimesRequest}
                onswitch={(option) => ($form.option = option)}
                onaddtime={$form.extra.length < 3 ? () => ($form.extra = [...$form.extra, '']) : undefined}
              />
              {#if $form.extra.length > 0 && $form.extra.length < 3}
                <Button type="button" variant="outline" class="w-fit" onclick={() => ($form.extra = [...$form.extra, ''])}><PlusIcon aria-hidden="true" />Add another one-off time</Button>
              {/if}
            {/if}
          {/if}
        {/if}
      {/if}
      <FailureText message={$errors._errors?.[0]} />
      {#if !nobodyCanInterview}
        <div class="flex justify-end gap-2">
          <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
          {#if people}
            <Button type="submit" size="lg" disabled={$submitting}><SendIcon aria-hidden="true" />Send</Button>
          {/if}
        </div>
      {/if}
    </form>
  </Sheet.Content>
</Sheet.Root>
