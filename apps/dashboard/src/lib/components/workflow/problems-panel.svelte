<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import { describePath } from '#lib/workflow.js';

  // The validator's errors in words, above a workflow's tabs; the `ValidationBadge` beside the title counts them.
  // Nothing renders while there are none.
  let { title, workflow, errors }: { title: string; workflow: HiringWorkflow; errors: readonly DefinitionError[] } = $props();
</script>

{#if errors.length > 0}
  <Panel {title} count={errors.length}>
    <ul class="flex flex-col gap-1">
      {#each errors as error, index (index)}
        <li><span class="font-semibold">{describePath(workflow, error.path)}:</span> {error.message}</li>
      {/each}
    </ul>
  </Panel>
{/if}
