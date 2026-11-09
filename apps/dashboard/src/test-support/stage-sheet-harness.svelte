<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import StageSheet from '#lib/components/workflow/stage-sheet.svelte';

  // One stage's sheet on its own, over a draft and an open stage the harness owns as the builder owns them, so the sheet's
  // edits change the workflow it reads. The builder's three ways out are callbacks a test can watch. The stages (id, name,
  // kind, limit) and the automation ids are printed as JSON, for what the sheet does not show. Render inside
  // with-query-client.svelte.
  let {
    workflow,
    stageId = null,
    errors = [],
    onscorecard,
    onautomations,
    onremove,
  }: {
    workflow: HiringWorkflow;
    stageId?: string | null;
    errors?: DefinitionError[];
    onscorecard?: (index: number) => void;
    onautomations?: (stageId: string) => void;
    onremove?: (index: number) => void;
  } = $props();

  let edited = $state(untrack(() => workflow));
  let open = $state<string | null>(untrack(() => stageId));
</script>

<StageSheet
  bind:workflow={edited}
  bind:stageId={open}
  {errors}
  automationsHref="?tab=automations"
  onscorecard={(index) => onscorecard?.(index)}
  onautomations={(id) => onautomations?.(id)}
  onremove={(index) => onremove?.(index)}
/>
<button type="button" onclick={() => (open = edited.stages[1]?.id ?? null)}>Open the second stage</button>
<p data-testid="open">{open ?? 'none'}</p>
<p data-testid="stages">{JSON.stringify(edited.stages.map((stage) => ({ id: stage.id, name: stage.name, kind: stage.kind, slaDays: stage.slaDays ?? null })))}</p>
<p data-testid="automations">{edited.automations.map((automation) => automation.id).join(',')}</p>
<p data-testid="automation-names">{edited.automations.map((automation) => automation.name).join('|')}</p>
