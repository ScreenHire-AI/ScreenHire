<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import { createListJobApplications } from '@screenhire/api-client/svelte-query';
  import PipelineBoard from '#lib/components/pipeline-board.svelte';
  import { createMoves } from '#lib/moves.svelte.js';

  // The job page's board wiring without the rest of the page: the job's applications from the API, moved through the
  // same move logic. Render inside with-query-client.svelte.
  let { jobId, stages }: { jobId: string; stages: WorkflowStage[] } = $props();

  const applications = createListJobApplications(() => jobId);
  const moves = createMoves();
</script>

{#if applications.data}
  <PipelineBoard applications={applications.data.data} {stages} {moves} />
{/if}
<p role="status">{moves.announcement}</p>
