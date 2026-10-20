<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import { createWorkflowDraft } from '#lib/workflow-draft.svelte.js';

  // A builder page's local draft without the page. The stored workflow is what the job query returns; the buttons
  // stand in for an edit, for another person's save arriving by refetch, and for the newer-version prompt. Render
  // inside with-query-client.svelte.
  let { first, theirs }: { first: HiringWorkflow; theirs: HiringWorkflow } = $props();

  let stored = $state(untrack(() => first));
  let version = $state(1);
  const draft = createWorkflowDraft();
  $effect(() => draft.load(stored, `job:${version}`));
</script>

<button type="button" onclick={() => (draft.workflow!.stages[1]!.name = 'Phone screen')}>Rename</button>
<button
  type="button"
  onclick={() => {
    stored = theirs;
    version = 2;
  }}>Someone else saves</button
>
<button type="button" onclick={() => draft.discardForNewer()}>Load theirs</button>
<p data-testid="second-stage">{draft.workflow?.stages[1]?.name}</p>
<p data-testid="dirty">{draft.dirty ? 'unsaved' : 'saved'}</p>
<p data-testid="outdated">{draft.outdated ? 'outdated' : 'current'}</p>
