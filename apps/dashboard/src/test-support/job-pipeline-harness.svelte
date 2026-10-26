<script lang="ts">
  import { untrack } from 'svelte';
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import JobPipeline from '#lib/components/job-pipeline.svelte';
  import { noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { createMoves } from '#lib/moves.svelte.js';

  // The job page's pipeline wiring without the rest of the page: the moves every control shares and the integrity filter
  // and view the page binds. Render inside with-query-client.svelte.
  let {
    stages,
    applications,
    integrityFilter: initialFilter = { ...noIntegrityFilter },
  }: { stages: WorkflowStage[]; applications: ApplicationSummary[]; integrityFilter?: IntegrityFilter } = $props();

  const moves = createMoves();
  let integrityFilter = $state<IntegrityFilter>(untrack(() => ({ ...initialFilter })));
  let view = $state<'table' | 'board'>('table');
</script>

<JobPipeline
  jobTitle="Senior .NET Engineer"
  {stages}
  {applications}
  pending={false}
  error={null}
  onretry={() => {}}
  {moves}
  bind:integrityFilter
  bind:view
/>
