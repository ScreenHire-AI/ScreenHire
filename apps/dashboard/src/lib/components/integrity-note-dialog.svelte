<script lang="ts">
  import { addIntegrityNote } from '@screenhire/api-client';
  import { AddIntegrityNoteBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { cn } from '#lib/utils.js';

  // A note on the application's integrity, kept in the review log with the author and the time.
  let {
    open = $bindable(false),
    applicationId,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    /** After the API answered: a sentence for the panel's status line. */
    ondone: (message: string) => void;
  } = $props();

  const schema = AddIntegrityNoteBody.extend({ note: z.string().trim().min(1, 'Write the note first.') });

  const { form, errors, enhance, submitting, reset } = superForm(defaults({ note: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    // A dialog's form belongs to the panel, not the page: leave the page's form state and its loads alone. The panel
    // refetches its own queries when the API has answered.
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      const response = await addIntegrityNote(applicationId, { note: form.data.note.trim() });
      if (response.status === 200) {
        open = false;
        ondone('Note added to the review log.');
      } else if (response.status === 400) {
        applyValidationProblem(form, response.data);
      } else {
        form.valid = false;
        form.errors._errors = ["We couldn't save the note just now. Try again in a minute."];
      }
    },
  });

  $effect(() => {
    if (open) reset({ data: { note: '' } });
  });
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Add a note</Dialog.Title>
      <Dialog.Description>Your team sees it in the review log, with your name and the time.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.note ? true : undefined}>
        <Field.Label for="integrity-note">Note</Field.Label>
        <Textarea
          id="integrity-note"
          name="note"
          rows={4}
          bind:value={$form.note}
          aria-invalid={$errors.note ? 'true' : undefined}
          aria-describedby={$errors.note ? 'integrity-note-error' : undefined}
        />
        <Field.Error id="integrity-note-error" errors={$errors.note?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <p role="alert" class="text-sm text-destructive">{$errors._errors[0]}</p>
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline' }))}>Cancel</Dialog.Close>
        <Button type="submit" disabled={$submitting}>Add note</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
