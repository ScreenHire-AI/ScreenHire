<script lang="ts">
  import AtSignIcon from '@lucide/svelte/icons/at-sign';
  import { addApplicationNote, editApplicationNote, type TeamMember } from '@screenhire/api-client';
  import { onMount } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import MentionField from '#lib/components/mention-field.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Kbd, KbdGroup } from '#lib/components/ui/kbd/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { toDisplay, toStored, type PickedMention } from '#lib/mentions.js';
  import { isApple } from '#lib/shortcuts.js';

  // A note for the hiring team (design system section 22): the box on Overview and Activity, and the same field in place
  // of a note while its author edits it. @ mentions a teammate (MentionField); the API stores each mention as the
  // member's id, and the person gets a card and an email that never quotes the note. Save is outline: the page's one
  // primary is Move to stage (A1). Ctrl or Cmd Enter saves; while editing, Esc cancels.
  let {
    applicationId,
    note,
    members,
    currentUserId,
    ondone,
    oncancel,
  }: {
    applicationId: string;
    /** The note being edited, with its stored text; without it the form adds a new note. */
    note?: { id: string; text: string };
    members: readonly TeamMember[];
    currentUserId: string | undefined;
    /** After the API answered: a sentence for the page's status line. */
    ondone: (message: string) => void | Promise<void>;
    oncancel?: () => void;
  } = $props();

  /** As the API allows it (JobApplication.MaxNoteLength), counted on the stored text. */
  const maxNoteLength = 2000;
  // Not `.trim()`: superforms stores the parsed value back into the field on a programmatic change, which cut the space
  // after a picked mention and glued the next word to the name. The text is trimmed when it is saved.
  const schema = z.object({ note: z.string().refine((text) => text.trim().length > 0, 'Write the note first.') });
  const id = $props.id();
  const fieldId = `note-${id}`;
  const saveKeys = isApple() ? ['⌘', 'Enter'] : ['Ctrl', 'Enter'];

  // svelte-ignore state_referenced_locally
  const initial = note ? toDisplay(note.text, members) : { text: '', picked: [] };
  let picked = $state<PickedMention[]>(initial.picked);
  let field = $state<MentionField>();
  let textarea = $state<HTMLTextAreaElement | null>(null);

  const { form, errors, enhance, submitting, submit } = superForm(defaults({ note: initial.text }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    // The note belongs to the page's Activity, not the page: leave the page's form state and loads alone.
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      const stored = toStored(form.data.note.trim(), picked);
      if (stored.length > maxNoteLength) {
        form.valid = false;
        form.errors.note = [`A note is at most ${maxNoteLength} characters.`];
        return;
      }
      const response = note
        ? await editApplicationNote(applicationId, note.id, { note: stored })
        : await addApplicationNote(applicationId, { note: stored });
      if (response.status === 200 || response.status === 201) {
        if (!note) {
          // Superforms puts this form's data back into the field once onUpdate returns, so a reset() here would be undone.
          form.data.note = '';
          picked = [];
        }
        await ondone(note ? 'Note saved.' : 'Note added.');
      } else if (response.status === 400) {
        applyValidationProblem(form, response.data);
      } else {
        form.valid = false;
        form.errors._errors = [
          response.status === 404
            ? 'This note was deleted.'
            : response.status === 409
              ? 'Someone changed this note at the same moment. Try again.'
              : "We couldn't save the note just now. Try again in a minute.",
        ];
      }
    },
  });

  onMount(() => {
    if (note && textarea) {
      textarea.focus();
      textarea.setSelectionRange(textarea.value.length, textarea.value.length);
    }
  });
</script>

<form method="POST" use:enhance novalidate class="flex min-w-0 flex-1 flex-col gap-2">
  <Field.Field data-invalid={$errors.note ? true : undefined}>
    <Field.Label for={fieldId} class="sr-only">{note ? 'Edit your note' : 'Note for the hiring team'}</Field.Label>
    <MentionField
      bind:this={field}
      bind:value={$form.note}
      bind:picked
      bind:ref={textarea}
      id={fieldId}
      rows={note ? 3 : 2}
      placeholder={note ? undefined : 'Add a note for the hiring team'}
      {members}
      {currentUserId}
      invalid={Boolean($errors.note)}
      describedby={$errors.note ? `${fieldId}-error` : undefined}
      onsave={() => submit()}
      oncancel={note ? oncancel : undefined}
    />
    <Field.Error id={`${fieldId}-error`} errors={$errors.note?.map((message) => ({ message }))} />
  </Field.Field>
  {#if $errors._errors}
    <FailureText message={$errors._errors[0]} />
  {/if}
  <div class="flex items-center justify-end gap-2">
    <div class="mr-auto md:hidden">
      <IconButton label="Mention someone" variant="outline" size="icon-lg" onclick={() => field?.startMention()}><AtSignIcon aria-hidden="true" /></IconButton>
    </div>
    {#if note}
      <Button type="button" variant="outline" onclick={oncancel}>Cancel</Button>
    {/if}
    <Tooltip.Root>
      <Tooltip.Trigger>
        {#snippet child({ props })}
          <Button {...props} type="submit" variant="outline" disabled={$submitting}>{note ? 'Save' : 'Save note'}</Button>
        {/snippet}
      </Tooltip.Trigger>
      <Tooltip.Content><KbdGroup>{#each saveKeys as key (key)}<Kbd>{key}</Kbd>{/each}</KbdGroup></Tooltip.Content>
    </Tooltip.Root>
  </div>
</form>
