<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import StageCards from '#lib/components/workflow/stage-cards.svelte';

  // The Pipeline tab's stage cards on their own, over a draft the harness owns as a page owns its own, so adding,
  // removing and moving stages change what the cards show. The page's three ways out are callbacks a test can watch. The
  // stage ids and the automation ids are printed in order, for what the cards do not show. Render inside
  // with-query-client.svelte (the cards' menus and grips use its tooltip provider).
  let {
    workflow,
    errors = [],
    onopen,
    onscorecard,
    onautomations,
  }: {
    workflow: HiringWorkflow;
    errors?: DefinitionError[];
    onopen?: (stageId: string) => void;
    onscorecard?: (index: number) => void;
    onautomations?: (stageId: string) => void;
  } = $props();

  let edited = $state(untrack(() => workflow));
</script>

<StageCards
  bind:workflow={edited}
  {errors}
  onopen={(id) => onopen?.(id)}
  onscorecard={(index) => onscorecard?.(index)}
  onautomations={(id) => onautomations?.(id)}
/>
<p data-testid="stage-ids">{edited.stages.map((stage) => stage.id).join(',')}</p>
<p data-testid="automation-ids">{edited.automations.map((automation) => automation.id).join(',')}</p>
