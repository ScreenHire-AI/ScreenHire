<script lang="ts">
  import StickyNoteIcon from '@lucide/svelte/icons/sticky-note';
  import { addApplicationNote } from '@screenhire/api-client';
  import { AddApplicationNoteBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { cn } from '#lib/utils.js';

  // A note from the hiring team on an application, in a sheet so the candidate stays behind it (design system section
  // 9). It lands in Activity with its author and the time; ApplicationNoteAdded shows it on other open dashboards.
  let {
    open = $bindable(false),
    applicationId,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    /** After the API answered: a sentence for the page's status line. */
    ondone: (message: string) => void | Promise<void>;
  } = $props();

  /** As the API allows it (JobApplication.MaxNoteLength). */
  const maxNoteLength = 2000;
  const schema = AddApplicationNoteBody.extend({
    note: z.string().trim().min(1, 'Write the note first.').max(maxNoteLength, `A note is at most ${maxNoteLength} characters.`),
  });

  const { form, errors, enhance, submitting, reset } = superForm(defaults({ note: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    // The sheet's form belongs to the page's Activity, not the page: leave the page's form state and loads alone.
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      const response = await addApplicationNote(applicationId, { note: form.data.note.trim() });
      if (response.status === 201) {
        open = false;
        await ondone('Note added.');
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

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Add a note</Sheet.Title>
      <Sheet.Description>Your team sees it in Activity, with your name and the time.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4 px-4 pb-4">
      <Field.Field data-invalid={$errors.note ? true : undefined}>
        <Field.Label for="application-note">Note</Field.Label>
        <Textarea
          id="application-note"
          name="note"
          rows={6}
          maxlength={maxNoteLength}
          bind:value={$form.note}
          aria-invalid={$errors.note ? 'true' : undefined}
          aria-describedby={$errors.note ? 'application-note-error' : undefined}
        />
        <Field.Error id="application-note-error" errors={$errors.note?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}><StickyNoteIcon aria-hidden="true" />Add note</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
