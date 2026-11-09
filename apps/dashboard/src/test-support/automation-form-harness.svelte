<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import AutomationForm from '#lib/components/workflow/automation-form.svelte';
  import { setUndo } from '#lib/undo-context.js';
  import { UndoOffer } from '#lib/undo.svelte.js';

  // One automation's form as the builder's drawer shows it, over a draft the harness owns. The app shell gives the form an
  // undo offer; `undoable` does the same here, and the offer's message and Undo are drawn beside the form (the shell draws
  // them in `UndoNotice`). Without it the form is as it is outside the shell, in a component test. Render inside
  // with-query-client.svelte.
  let { workflow, undoable = true }: { workflow: HiringWorkflow; undoable?: boolean } = $props();

  let edited = $state(untrack(() => workflow));
  const offer = new UndoOffer(60_000);
  if (untrack(() => undoable)) setUndo(offer);
</script>

<AutomationForm bind:automation={edited.automations[0]!} index={0} workflow={edited} errors={[]} onremove={() => {}} />
<button type="button">Somewhere else</button>
<p data-testid="offer">{offer.message}</p>
{#if offer.message}<button type="button" onclick={() => offer.run()}>Undo</button>{/if}
<p data-testid="actions">{JSON.stringify(edited.automations[0]!.actions)}</p>
