<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import TryIt from '#lib/components/workflow/try-it.svelte';

  // The Try it tab on its own, over a draft the harness owns as a page owns its own. A page gets its problems from the API's
  // validator; the harness stands one in: an Add a tag step with no tag is a problem, and so is whatever `extra` holds, so
  // fixing the step in the notice makes the problem go, as it does in the app. The page's callbacks are watchable. Render
  // inside with-query-client.svelte.
  let {
    workflow,
    extra = [],
    editable = true,
    finish,
    onchange,
    onfix,
  }: {
    workflow: HiringWorkflow;
    extra?: DefinitionError[];
    editable?: boolean;
    finish?: { label: string; run: () => void; disabled?: boolean };
    onchange?: (stageId: string) => void;
    onfix?: (path: string) => void;
  } = $props();

  let edited = $state(untrack(() => workflow));
  const blankTags = $derived(
    edited.automations.flatMap((automation, a) =>
      automation.actions.flatMap((action, b) =>
        action.type === 'AddTag' && !action.tag?.trim()
          ? [{ path: `automations[${a}].actions[${b}]`, message: `The tag in "${automation.name}" has no name. Type one, or remove that step.` }]
          : [],
      ),
    ),
  );
  const errors = $derived([...blankTags, ...extra]);
</script>

<TryIt bind:workflow={edited} {errors} {editable} {finish} {onchange} onfix={(path) => onfix?.(path)} />
<button type="button" onclick={() => (edited.stages[1]!.name = 'Renamed')}>Rename a stage</button>
<p data-testid="automation-count">{edited.automations.length}</p>
