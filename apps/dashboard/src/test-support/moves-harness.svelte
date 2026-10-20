<script lang="ts">
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import { createMoves } from '#lib/moves.svelte.js';

  // The job page's bulk move without the rest of the page: one button moves every application given to one stage.
  // Render inside with-query-client.svelte.
  let { applications, to }: { applications: ApplicationSummary[]; to: WorkflowStage } = $props();

  const moves = createMoves();
  let stillSelected = $state<string[] | null>(null);
</script>

<button type="button" onclick={async () => (stillSelected = await moves.moveAll(applications, to))}>Move all</button>
<p role="status">{moves.announcement}</p>
{#if stillSelected}
  <ul aria-label="Still selected">
    {#each stillSelected as id (id)}<li>{id}</li>{/each}
  </ul>
{/if}
