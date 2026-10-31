<script lang="ts">
  import { explainIntegrityReason, ExplainReason } from '@screenhire/api-client';
  import { ExplainIntegrityReasonBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { explainReasonLabels } from '#lib/integrity.js';
  import { cn } from '#lib/utils.js';

  // Marking a reason explained records a person's reading of it in the review log. It changes nothing else: the level,
  // the candidate's status and every list stay as assessed until someone dismisses the assessment (ADR-0004).
  let {
    open = $bindable(false),
    applicationId,
    reason,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    /** The reason being explained: its assessment, its signal and its title. */
    reason: { assessmentId: string; signalId: string; title: string } | null;
    /** After the API answered: a sentence for the panel's status line, and whether the explanation was recorded. */
    ondone: (message: string, recorded: boolean) => void;
  } = $props();

  // The API also refuses `Other` without a note; checking here puts the message on the field before the round trip.
  const schema = ExplainIntegrityReasonBody.superRefine((body, context) => {
    if (body.reason === 'Other' && !body.note?.trim()) {
      context.addIssue({ code: 'custom', path: ['note'], message: 'Say why, in a few words, when the reason is "Other".' });
    }
  });

  const { form, errors, enhance, submitting, reset } = superForm(defaults({ reason: 'ExpectedForRole', note: '' }, zod4(schema)), {
    id: 'integrity-explain',
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    // A dialog's form belongs to the panel, not the page (as the dismiss dialog's does).
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid || !reason) return;
      const note = form.data.note?.trim() || null;
      const response = await explainIntegrityReason(applicationId, reason.assessmentId, reason.signalId, { reason: form.data.reason, note });
      if (response.status === 200) {
        open = false;
        ondone(`Marked explained: ${reason.title}. It is in the review log.`, true);
      } else if (response.status === 400) {
        applyValidationProblem(form, response.data);
      } else if (response.status === 409) {
        open = false;
        ondone('Someone already explained this reason, or the assessment changed. The panel now shows the latest.', false);
      } else {
        form.valid = false;
        form.errors._errors = ["We couldn't record the explanation just now. Try again in a minute."];
      }
    },
  });

  // Each opening starts from a clean form.
  $effect(() => {
    if (open) reset({ data: { reason: 'ExpectedForRole', note: '' } });
  });

  const reasons = Object.values(ExplainReason);
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Mark "{reason?.title}" explained?</Dialog.Title>
      <Dialog.Description>It goes in the review log. The level stays as assessed.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Set>
        <Field.Legend variant="label">Reason</Field.Legend>
        <div role="radiogroup" aria-label="Reason" class="flex flex-col gap-2">
          {#each reasons as choice (choice)}
            <ChoiceCard name="explain-reason" value={choice} bind:group={$form.reason} title={explainReasonLabels[choice]} />
          {/each}
        </div>
      </Field.Set>
      <Field.Field data-invalid={$errors.note ? true : undefined}>
        <Field.Label for="explain-note">Note {$form.reason === 'Other' ? '(required)' : '(optional)'}</Field.Label>
        <Textarea
          id="explain-note"
          name="note"
          rows={3}
          bind:value={$form.note}
          aria-invalid={$errors.note ? 'true' : undefined}
          aria-describedby={$errors.note ? 'explain-note-error' : undefined}
        />
        <Field.Error id="explain-note-error" errors={$errors.note?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}>Mark explained</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
