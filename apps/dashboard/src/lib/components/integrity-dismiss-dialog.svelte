<script lang="ts">
  import CircleSlashIcon from '@lucide/svelte/icons/circle-slash';
  import { dismissIntegrityAssessment, DismissReason } from '@screenhire/api-client';
  import { DismissIntegrityAssessmentBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { assessmentNames, dismissReasonLabels, type AssessmentKind } from '#lib/integrity.js';
  import { cn } from '#lib/utils.js';

  // Dismissing records a reviewer's decision in the audit log. It never changes the assessment or its evidence, and
  // it is the only way to set an assessment aside: there is no reject here (ADR-0004).
  let {
    open = $bindable(false),
    applicationId,
    assessment,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    assessment: { id: string; kind: AssessmentKind } | null;
    /** After the API answered: a sentence for the panel's status line. */
    ondone: (message: string) => void;
  } = $props();

  // The API also refuses `Other` without a note; checking here puts the message on the field before the round trip.
  const schema = DismissIntegrityAssessmentBody.superRefine((body, context) => {
    if (body.reason === 'Other' && !body.note?.trim()) {
      context.addIssue({ code: 'custom', path: ['note'], message: 'Say why, in a few words, when the reason is "Other".' });
    }
  });

  const { form, errors, enhance, submitting, reset } = superForm(defaults({ reason: 'FalsePositive', note: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    // A dialog's form belongs to the panel, not the page: leave the page's form state and its loads alone. The panel
    // refetches its own queries when the API has answered.
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid || !assessment) return;
      const note = form.data.note?.trim() || null;
      const response = await dismissIntegrityAssessment(applicationId, assessment.id, { reason: form.data.reason, note });
      if (response.status === 200) {
        open = false;
        ondone(`Dismissed ${assessmentNames[assessment.kind].toLowerCase()}: ${dismissReasonLabels[form.data.reason]}. The decision is in the review log.`);
      } else if (response.status === 400) {
        applyValidationProblem(form, response.data);
      } else if (response.status === 409) {
        open = false;
        ondone('This assessment was already dismissed, or a newer one replaced it. The panel now shows the latest.');
      } else {
        form.valid = false;
        form.errors._errors = ["We couldn't record the dismissal just now. Try again in a minute."];
      }
    },
  });

  // Each opening starts from a clean form.
  $effect(() => {
    if (open) reset({ data: { reason: 'FalsePositive', note: '' } });
  });

  const reasons = Object.values(DismissReason);
  const name = $derived(assessment ? assessmentNames[assessment.kind].toLowerCase() : 'assessment');
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Dismiss this {name} assessment?</Dialog.Title>
      <Dialog.Description>
        The evidence stays on record and your reason goes in the review log{assessment?.kind === 'authenticity' ? '; the candidate leaves "To review".' : '.'}
      </Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Set>
        <Field.Legend variant="label">Reason</Field.Legend>
        <div role="radiogroup" aria-label="Reason" class="flex flex-col gap-2">
          {#each reasons as reason (reason)}
            <ChoiceCard name="reason" value={reason} bind:group={$form.reason} title={dismissReasonLabels[reason]} />
          {/each}
        </div>
      </Field.Set>
      <Field.Field data-invalid={$errors.note ? true : undefined}>
        <Field.Label for="dismiss-note">Note {$form.reason === 'Other' ? '(required)' : '(optional)'}</Field.Label>
        <Textarea
          id="dismiss-note"
          name="note"
          rows={3}
          bind:value={$form.note}
          aria-invalid={$errors.note ? 'true' : undefined}
          aria-describedby={$errors.note ? 'dismiss-note-error' : undefined}
        />
        <Field.Error id="dismiss-note-error" errors={$errors.note?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}><CircleSlashIcon aria-hidden="true" />Dismiss with this reason</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
