<script lang="ts">
  import SendIcon from '~icons/lucide/send';
  import XIcon from '~icons/lucide/x';
  import type { ApplicationInterview, HttpValidationProblemDetails, OpenTimesRequest } from '@screenhire/api-client';
  import { ApiError, createChangeInterviewOption } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, setError, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import OpenTimesCheck from '#lib/components/open-times-check.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import WeeklyHoursEditor from '#lib/components/weekly-hours-editor.svelte';
  import WhoIsFree from '#lib/components/who-is-free.svelte';
  import { applyValidationProblem } from '#lib/forms.js';
  import { defaultWindow, optionOrder, optionPayload, optionWords, pickedInstants, scheduleSchema, weekdayHours, weekly } from '#lib/interview-options.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';
  import { durationLabel, windowInstants, zoneLabel } from '#lib/schedule-interview.js';
  import { cn } from '#lib/utils.js';

  // Change how they pick, and Move (docs/architecture/interview-scheduling.md, "Changing course"), in a sheet (design system
  // section 9): the same three ways as Schedule interview, for an interview that is already offered. Its interviewers, length
  // and time zone stay. An open interview takes the new way at once; a booked one keeps its time until a new one is booked.
  // The candidate's link stays the same, and they are emailed that their options changed.
  let {
    open = $bindable(false),
    applicationId,
    jobId,
    interview,
    candidateName,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    jobId: string;
    interview: ApplicationInterview;
    candidateName: string;
    ondone?: (message: string) => void;
  } = $props();

  const queryClient = useQueryClient();
  const change = createChangeInterviewOption(() => ({
    mutation: { onSettled: () => invalidatePath(queryClient, `${applicationsPath}/${applicationId}`) },
  }));

  // The interview's own zone, length and people: they do not change, and the panel makes a new sheet for another ({#key}).
  const fixed = untrack(() => ({
    zone: interview.timeZone,
    minutes: interview.durationMinutes,
    people: interview.interviewers.map((person) => person.userId),
    booked: interview.status === 'Booked',
  }));
  const firstName = $derived(candidateName.split(' ')[0] || candidateName);
  const nameOf = (userId: string) => interview.interviewers.find((person) => person.userId === userId)?.displayName ?? 'A teammate';

  const empty = () => ({
    option: interview.option ?? 'OurTimes',
    interviewerUserIds: fixed.people,
    durationMinutes: fixed.minutes,
    ...defaultWindow(Date.now(), fixed.zone, 14),
    timeZone: fixed.zone,
    hours: interview.hours?.days ?? weekdayHours(interview.dayStart, interview.dayEnd),
    useBlocks: interview.useBlocks ?? false,
    proposed: ['', ''],
    extra: [] as string[],
  });

  const { form, errors, enhance, submitting, reset } = superForm(defaults(empty(), zod4(scheduleSchema)), {
    SPA: true,
    validators: zod4(scheduleSchema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    dataType: 'json',
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const data = optionPayload(submitted.data, Date.now());
      if (!data) return;
      try {
        await change.mutateAsync({ interviewId: interview.id, data });
        open = false;
        ondone?.(fixed.booked ? `Asked ${candidateName} to pick a new time.` : `Changed how ${candidateName} picks a time.`);
      } catch (error) {
        submitted.valid = false;
        if (error instanceof ApiError && error.status === 400) {
          const problem = error.body as HttpValidationProblemDetails;
          const lists = { hours: 'hours._errors', proposedTimes: 'proposed._errors', extraTimes: 'extra._errors' } as const;
          for (const [name, path] of Object.entries(lists)) {
            for (const message of problem.errors?.[name] ?? []) setError(submitted, path, message);
          }
          applyValidationProblem(submitted, { ...problem, errors: Object.fromEntries(Object.entries(problem.errors ?? {}).filter(([name]) => !(name in lists))) });
        } else if (error instanceof ApiError && error.status === 422) {
          submitted.errors._errors = ['Someone on this interview has no working calendar and no interview hours, so times cannot be read from calendars. Use interview blocks, or another way.'];
        } else if (error instanceof ApiError && error.status === 404) {
          submitted.errors._errors = ['Scheduling is not set up here, so the candidate could not be emailed.'];
        } else {
          submitted.errors._errors = [describeError(error, 'Could not change how they pick')];
        }
      }
    },
  });

  // Starts fresh each time it opens.
  $effect(() => {
    if (!open) return;
    untrack(() => reset({ data: empty() }));
  });

  const openTimesRequest = $derived.by((): OpenTimesRequest | undefined => {
    if ($form.option !== 'OurTimes') return undefined;
    const window = windowInstants($form.windowStart, $form.windowEnd, fixed.zone);
    if (!window || $form.hours.length === 0) return undefined;
    return { jobId, interviewerUserIds: fixed.people, durationMinutes: fixed.minutes, ...window, timeZone: fixed.zone, hours: weekly($form.hours), useBlocks: $form.useBlocks };
  });
  const proposedStarts = $derived((pickedInstants($form.proposed, fixed.zone) ?? []).map((instant) => new Date(instant).toISOString()));
  const describedBy = (id: string, messages: string[] | undefined) => (messages ? id : undefined);
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="overflow-y-auto data-[side=right]:w-full data-[side=right]:sm:max-w-xl">
    <Sheet.Header>
      <Sheet.Title>{fixed.booked ? 'Move the interview' : 'Change how they pick'}</Sheet.Title>
      <Sheet.Description>
        {fixed.booked ? 'The booked time stays until a new one is booked.' : `${firstName}'s link stays the same, and they are emailed the change.`}
      </Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-5 px-4 pb-4" data-change-option>
      <p class="text-muted-foreground">{durationLabel(fixed.minutes)}, in {zoneLabel(fixed.zone)}.</p>
      <div class="flex flex-col gap-2">
        <span id="change-option-label" class="text-body font-medium">How should {firstName} pick a time?</span>
        <div role="radiogroup" aria-labelledby="change-option-label" class="flex flex-col gap-2">
          {#each optionOrder as option (option)}
            <ChoiceCard name="change-option" value={option} bind:group={$form.option} title={optionWords[option].title} description={optionWords[option].description} />
          {/each}
        </div>
      </div>

      {#if $form.option === 'OurSuggestion'}
        <fieldset class="flex min-w-0 flex-col gap-3" aria-describedby={$errors.proposed?._errors ? 'change-proposed-error' : undefined}>
          <legend class="mb-1 text-body font-medium">Times you suggest</legend>
          <div class="grid gap-5 sm:grid-cols-2">
            <Field.Field>
              <Field.Label for="change-proposed-0">First time</Field.Label>
              <Input id="change-proposed-0" type="datetime-local" bind:value={$form.proposed[0]} />
            </Field.Field>
            <Field.Field>
              <Field.Label for="change-proposed-1">Second time (optional)</Field.Label>
              <Input id="change-proposed-1" type="datetime-local" bind:value={$form.proposed[1]} />
            </Field.Field>
          </div>
          <Field.Error id="change-proposed-error" errors={$errors.proposed?._errors?.map((message) => ({ message }))} />
          {#if proposedStarts.length > 0}
            <WhoIsFree {jobId} interviewerUserIds={fixed.people} starts={proposedStarts} durationMinutes={fixed.minutes} interviewId={interview.id} {nameOf} label="Who is free" />
          {/if}
        </fieldset>
      {:else}
        <div class="grid gap-5 sm:grid-cols-2">
          <Field.Field data-invalid={$errors.windowStart ? true : undefined}>
            <Field.Label for="change-first-day">First day</Field.Label>
            <Input id="change-first-day" type="date" bind:value={$form.windowStart} aria-invalid={$errors.windowStart ? 'true' : undefined} aria-describedby={describedBy('change-first-day-error', $errors.windowStart)} />
            <Field.Error id="change-first-day-error" errors={$errors.windowStart?.map((message) => ({ message }))} />
          </Field.Field>
          <Field.Field data-invalid={$errors.windowEnd ? true : undefined}>
            <Field.Label for="change-last-day">Last day</Field.Label>
            <Input id="change-last-day" type="date" bind:value={$form.windowEnd} aria-invalid={$errors.windowEnd ? 'true' : undefined} aria-describedby={describedBy('change-last-day-error', $errors.windowEnd)} />
            <Field.Error id="change-last-day-error" errors={$errors.windowEnd?.map((message) => ({ message }))} />
          </Field.Field>
        </div>

        {#if $form.option === 'OurTimes'}
          <div class="flex flex-col gap-2">
            <span id="change-source-label" class="text-body font-medium">Open times come from</span>
            <div role="radiogroup" aria-labelledby="change-source-label" class="grid gap-2 sm:grid-cols-2">
              <label class="flex items-center gap-2 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring">
                <input type="radio" name="change-use-blocks" class="size-4 accent-primary" checked={!$form.useBlocks} onchange={() => ($form.useBlocks = false)} />
                Their calendars
              </label>
              <label class="flex items-center gap-2 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring">
                <input type="radio" name="change-use-blocks" class="size-4 accent-primary" checked={$form.useBlocks} onchange={() => ($form.useBlocks = true)} />
                Interview blocks
              </label>
            </div>
          </div>
        {/if}

        <WeeklyHoursEditor
          bind:value={$form.hours}
          id="change-hours"
          legend={$form.option === 'TheirTimes' ? 'Hours they may pick from' : $form.useBlocks ? 'Interview blocks' : 'Working hours'}
          errors={$errors.hours?._errors}
        />

        {#if $form.option === 'OurTimes'}
          {#each $form.extra as _, index (index)}
            <div class="flex items-end gap-2">
              <Field.Field class="min-w-0 flex-1">
                <Field.Label for={`change-extra-${index}`}>One-off time</Field.Label>
                <Input id={`change-extra-${index}`} type="datetime-local" bind:value={$form.extra[index]} />
              </Field.Field>
              <Button type="button" variant="ghost" size="icon-lg" onclick={() => ($form.extra = $form.extra.filter((__, at) => at !== index))}>
                <XIcon aria-hidden="true" /><span class="sr-only">Remove this one-off time</span>
              </Button>
            </div>
          {/each}
          <Field.Error id="change-extra-error" errors={$errors.extra?._errors?.map((message) => ({ message }))} />
          <OpenTimesCheck
            request={openTimesRequest}
            onswitch={(option) => ($form.option = option)}
            onaddtime={$form.extra.length < 3 ? () => ($form.extra = [...$form.extra, '']) : undefined}
          />
        {/if}
      {/if}
      <FailureText message={$errors._errors?.[0]} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}><SendIcon aria-hidden="true" />Send</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
