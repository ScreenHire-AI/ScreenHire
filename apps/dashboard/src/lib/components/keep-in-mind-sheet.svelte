<script lang="ts">
  import BookmarkIcon from '~icons/lucide/bookmark';
  import { createKeepInMind } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { formatDate } from '#lib/dates.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, candidatesPath, jobsPath, talentPoolPath } from '#lib/realtime.js';
  import { describeKept } from '#lib/talent-pool.js';
  import { cn } from '#lib/utils.js';

  // Keep in mind (docs/architecture/talent-pool.md), in a sheet (design system section 9): for one candidate from Move
  // to stage on their page, or for a selection from a job's selection bar. They leave the job without a rejection and
  // join the talent pool until their permission runs out. The note says why, once for all of them; it stays on the Kept
  // in mind entry in Activity and the pool shows it. Never reads Verification output (ADR-0004).
  let {
    open = $bindable(false),
    applicationIds,
    candidateName,
    until,
    ondone,
  }: {
    open?: boolean;
    applicationIds: readonly string[];
    /** The one candidate's name, for a single keep. */
    candidateName?: string;
    /** When the one candidate's permission runs out. */
    until?: string | null;
    /** What happened, for the page's live region. */
    ondone?: (message: string) => void;
  } = $props();

  /** As the API allows it (KeepInMindRequestValidator). */
  const maxNoteLength = 2000;
  const queryClient = useQueryClient();
  const keep = createKeepInMind(() => ({
    mutation: {
      onSettled: () => Promise.all([talentPoolPath, jobsPath, applicationsPath, candidatesPath].map((path) => invalidatePath(queryClient, path))),
    },
  }));
  let note = $state('');
  let failure = $state('');

  $effect(() => {
    if (!open) return;
    untrack(() => {
      note = '';
      failure = '';
    });
  });

  const title = $derived(candidateName ? `Keep ${candidateName} in mind` : `Keep ${applicationIds.length} in mind`);
  const description = $derived(
    candidateName && until
      ? `They leave this job without a rejection and stay in the talent pool until ${formatDate(until)}.`
      : 'They leave the job without a rejection. Only candidates who allowed it join the talent pool.',
  );

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    failure = '';
    try {
      const response = await keep.mutateAsync({ data: { applicationIds: [...applicationIds], note: note.trim() || null } });
      ondone?.(describeKept(response.data, candidateName));
      open = false;
    } catch (error) {
      failure = describeError(error, 'Could not keep them in mind');
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>{title}</Sheet.Title>
      <Sheet.Description>{description}</Sheet.Description>
    </Sheet.Header>
    <form novalidate class="flex flex-col gap-4 px-4 pb-4" {onsubmit}>
      <Field.Field>
        <Field.Label for="keep-in-mind-note">Why</Field.Label>
        <Textarea id="keep-in-mind-note" rows={4} maxlength={maxNoteLength} bind:value={note} />
        <Field.Description>The talent pool shows it beside them.</Field.Description>
      </Field.Field>
      <FailureText message={failure} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={keep.isPending}><BookmarkIcon aria-hidden="true" />Keep in mind</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
