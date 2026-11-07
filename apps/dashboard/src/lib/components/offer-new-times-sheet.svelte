<script lang="ts">
  import SendIcon from '~icons/lucide/send';
  import type { ApplicationInterview, HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createOfferNewInterviewTimes } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';
  import { defaultDays, durationLabel, newTimesSchema, timeLabel, windowInstants, zoneLabel } from '#lib/schedule-interview.js';
  import { cn } from '#lib/utils.js';

  // Offer new times (docs/architecture/interview-scheduling.md), in a sheet (design system section 9): the days the candidate may
  // choose from for a booked interview. The length, working hours and time zone are the interview's own, so only the days are
  // asked. The candidate is emailed a link; the booked time stays held on every calendar until they pick, and the events then
  // move on their own.
  let {
    open = $bindable(false),
    applicationId,
    interview,
    candidateName,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    interview: ApplicationInterview;
    candidateName: string;
    /** After the new times were offered: a sentence for the page's status line. */
    ondone?: (message: string) => void;
  } = $props();

  const queryClient = useQueryClient();
  const offer = createOfferNewInterviewTimes(() => ({
    mutation: { onSettled: () => invalidatePath(queryClient, `${applicationsPath}/${applicationId}`) },
  }));

  // The interview's own zone: it does not change, and the panel makes a new sheet for another interview ({#key}).
  const zone = untrack(() => interview.timeZone);
  // Tomorrow to two weeks ahead, in that zone, until the person says otherwise.
  const empty = () => defaultDays(Date.now(), zone);

  const { form, errors, enhance, submitting, reset } = superForm(defaults(empty(), zod4(newTimesSchema(zone))), {
    SPA: true,
    validators: zod4(newTimesSchema(zone)),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      if (!submitted.valid) return;
      const instants = windowInstants(submitted.data.windowStart, submitted.data.windowEnd, zone);
      if (!instants) return;
      try {
        await offer.mutateAsync({ interviewId: interview.id, data: instants });
        open = false;
        ondone?.(`Offered ${candidateName} new times to choose from.`);
      } catch (error) {
        submitted.valid = false;
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else if (error instanceof ApiError && error.status === 404) {
          submitted.errors._errors = ['Scheduling is not set up here, so the candidate could not be sent a link.'];
        } else {
          // 409 carries its own words: the interview is no longer booked, or the application ended.
          submitted.errors._errors = [describeError(error, 'Could not offer new times')];
        }
      }
    },
  });

  // Starts fresh each time it opens.
  $effect(() => {
    if (!open) return;
    untrack(() => reset({ data: empty() }));
  });

  const describedBy = (id: string, messages: string[] | undefined) => (messages ? id : undefined);
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header>
      <Sheet.Title>Offer new times</Sheet.Title>
      <Sheet.Description>{candidateName} gets a link to pick another time.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-5 px-4 pb-4" data-offer-new-times>
      <div class="grid gap-5 sm:grid-cols-2">
        <Field.Field data-invalid={$errors.windowStart ? true : undefined}>
          <Field.Label for="new-times-first-day">First day</Field.Label>
          <Input
            id="new-times-first-day"
            name="windowStart"
            type="date"
            bind:value={$form.windowStart}
            aria-invalid={$errors.windowStart ? 'true' : undefined}
            aria-describedby={describedBy('new-times-first-day-error', $errors.windowStart)}
          />
          <Field.Error id="new-times-first-day-error" errors={$errors.windowStart?.map((message) => ({ message }))} />
        </Field.Field>
        <Field.Field data-invalid={$errors.windowEnd ? true : undefined}>
          <Field.Label for="new-times-last-day">Last day</Field.Label>
          <Input
            id="new-times-last-day"
            name="windowEnd"
            type="date"
            bind:value={$form.windowEnd}
            aria-invalid={$errors.windowEnd ? 'true' : undefined}
            aria-describedby={describedBy('new-times-last-day-error', $errors.windowEnd)}
          />
          <Field.Error id="new-times-last-day-error" errors={$errors.windowEnd?.map((message) => ({ message }))} />
        </Field.Field>
      </div>
      <p class="min-w-0 break-words text-muted-foreground">
        Same hours as before: {timeLabel(interview.dayStart)} to {timeLabel(interview.dayEnd)} on weekdays, {zoneLabel(interview.timeZone)}, {durationLabel(interview.durationMinutes)}.
        The booked time stays until they pick.
      </p>
      <FailureText message={$errors._errors?.[0]} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}><SendIcon aria-hidden="true" />Send new times</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
