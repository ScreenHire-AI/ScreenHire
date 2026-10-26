<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import { createListScorecardTemplates } from '@screenhire/api-client/svelte-query';

  // The name of a stage's scorecard: a job's copy says its own, a template's stage names one in the library.
  let { stage }: { stage: WorkflowStage } = $props();

  const library = createListScorecardTemplates();
  const name = $derived(
    stage.scorecard?.name ?? library.data?.data.find((scorecard) => scorecard.id === stage.scorecardTemplateId)?.name ?? 'a scorecard from the library',
  );
</script>

{name}
