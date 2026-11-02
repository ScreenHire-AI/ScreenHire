<script lang="ts">
  import HistoryIcon from '~icons/lucide/history';
  import { getGetSavedNoteHistoryQueryOptions, createEditSavedNote, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import type { TalentPoolMember } from '@screenhire/api-client';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { page as appPage } from '$app/state';
  import FailureText from '#lib/components/failure-text.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { talentPoolPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // Edit a saved candidate's note (docs/architecture/talent-pool.md), in a sheet (design system section 9), from Saved
  // candidates. The note is the team's own words about why they saved the person; a blank note clears it. The first note
  // stays in their Activity, and every change is kept in History, which opens from here (T6). Never reads Verification
  // output (ADR-0004).
  let {
    open = $bindable(false),
    person,
    ondone,
  }: {
    open?: boolean;
    /** The saved candidate whose note is edited. */
    person: TalentPoolMember | null;
    /** What happened, for the page's live region. */
    ondone?: (message: string) => void;
  } = $props();

  /** As the API allows it (EditSavedNoteRequestValidator). */
  const maxNoteLength = 2000;
  const schema = z.object({ note: z.string().max(maxNoteLength, `A note is at most ${maxNoteLength} characters.`) });
  const queryClient = useQueryClient();
  const edit = createEditSavedNote(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, talentPoolPath) } }));
  // Names the editor in the Last updated line, so it loads only while the sheet is open.
  const team = createListTeamMembers(() => ({ query: { enabled: open } }));
  const id = $props.id();

  let historyOpen = $state(false);
  const history = createHistoryPages(
    (before) => getGetSavedNoteHistoryQueryOptions(person?.candidateId ?? '', { before }),
    () => historyOpen && Boolean(person),
  );

  const { form, errors, enhance, submitting, reset } = superForm(defaults({ note: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    // The note belongs to the list, not the page: leave the page's form state and loads alone.
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid || !person) return;
      const target = person;
      try {
        await edit.mutateAsync({ candidateId: target.candidateId, data: { note: form.data.note.trim() || null } });
      } catch (error) {
        form.valid = false;
        form.errors._errors = [describeError(error, 'Could not save the note')];
        return;
      }
      open = false;
      ondone?.(`Saved the note for ${target.candidateName}.`);
    },
  });

  // Starts with the person's current note each time it opens; a live refetch while it is open keeps what was typed.
  $effect(() => {
    if (!open) return;
    untrack(() => reset({ data: { note: person?.note ?? '' } }));
  });
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Edit note for {person?.candidateName ?? ''}</Sheet.Title>
      <Sheet.Description>The first note stays in their Activity.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4 px-4 pb-4">
      <Field.Field data-invalid={$errors.note ? true : undefined}>
        <Field.Label for={`saved-note-${id}`}>Note</Field.Label>
        <Textarea
          id={`saved-note-${id}`}
          name="note"
          rows={5}
          maxlength={maxNoteLength}
          bind:value={$form.note}
          aria-invalid={$errors.note ? 'true' : undefined}
          aria-describedby={$errors.note ? `saved-note-${id}-error` : undefined}
        />
        <Field.Error id={`saved-note-${id}-error`} errors={$errors.note?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      {#if person?.noteUpdated}
        <span class="flex min-w-0 items-center gap-1 text-muted-foreground" data-last-updated>
          <LastUpdated value={person.noteUpdated} lead members={team.data?.data} currentUserId={appPage.data.user?.id} onhistory={() => (historyOpen = true)} />
        </span>
      {:else}
        <div>
          <Button type="button" variant="outline" size="sm" aria-haspopup="dialog" onclick={() => (historyOpen = true)}><HistoryIcon aria-hidden="true" />History</Button>
        </div>
      {/if}
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}>Save note</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>

<HistorySheet
  bind:open={historyOpen}
  name={`Note for ${person?.candidateName ?? ''}`}
  entries={history.entries}
  error={history.error}
  onretry={history.retry}
  hasMore={history.hasMore}
  loadingMore={history.loadingMore}
  onmore={history.more}
  members={team.data?.data}
  currentUserId={appPage.data.user?.id}
/>
