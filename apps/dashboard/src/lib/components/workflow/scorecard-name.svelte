<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import LibraryScorecardName from './library-scorecard-name.svelte';

  // The name of a stage's scorecard: a job's copy says its own, a template's stage names one in the library. Only the
  // second reads the library, so a stage with its own copy needs no request.
  let { stage }: { stage: WorkflowStage } = $props();
</script>

{#if stage.scorecard}{stage.scorecard.name}{:else}<LibraryScorecardName id={stage.scorecardTemplateId} />{/if}
