<script lang="ts">
  import type { Automation, HiringWorkflow } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import AutomationForm from '#lib/components/workflow/automation-form.svelte';
  import { UndoOffer } from '#lib/undo.svelte.js';

  // One automation's form as the builder's sheet shows it, over a draft the harness owns. The editor gives the form a way to
  // offer Undo; `undoable` does the same here, and the offer's message and Undo are drawn beside the form (the sheet draws
  // them in `UndoNotice`). Without it nothing is offered. Render inside with-query-client.svelte.
  let { workflow, undoable = true }: { workflow: HiringWorkflow; undoable?: boolean } = $props();

  let edited = $state(untrack(() => workflow));
  const offer = new UndoOffer(60_000);
  const onundo = (message: string, restore: (current: Automation) => Automation) =>
    offer.offer(message, async () => {
      edited.automations[0] = restore(edited.automations[0]!);
      return null;
    });
</script>

<AutomationForm bind:automation={edited.automations[0]!} index={0} workflow={edited} errors={[]} onundo={undoable ? onundo : undefined} />
<button type="button">Somewhere else</button>
<p data-testid="offer">{offer.message}</p>
{#if offer.message}<button type="button" onclick={() => offer.run()}>Undo</button>{/if}
<p data-testid="actions">{JSON.stringify(edited.automations[0]!.actions)}</p>
