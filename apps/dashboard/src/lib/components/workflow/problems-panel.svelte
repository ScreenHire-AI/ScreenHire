<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { describePath } from '#lib/workflow.js';

  // The validator's errors in words, above a workflow's tabs; the `ValidationBadge` beside the title says there are some,
  // and its Fix button brings the focus here. A message names its own thing, so it is shown as it is, with nothing
  // before it. Where the workflow can be edited, each problem has a Fix button that opens the place it is in and focuses
  // the field (`onfix`). Nothing renders while there are none.
  let {
    title,
    workflow,
    errors,
    onfix,
  }: { title: string; workflow: HiringWorkflow; errors: readonly DefinitionError[]; onfix?: (path: string) => void } = $props();
</script>

{#if errors.length > 0}
  <Panel {title} count={errors.length}>
    <ul class="flex flex-col">
      {#each errors as error, index (index)}
        <li class="flex items-center justify-between gap-3 border-b border-border-subtle py-2 first:pt-0 last:border-0 last:pb-0">
          <span class="min-w-0">{error.message}</span>
          {#if onfix}
            <Button variant="outline" data-problem-fix onclick={() => onfix(error.path)}>Fix<span class="sr-only">: {describePath(workflow, error.path)}</span></Button>
          {/if}
        </li>
      {/each}
    </ul>
  </Panel>
{/if}
