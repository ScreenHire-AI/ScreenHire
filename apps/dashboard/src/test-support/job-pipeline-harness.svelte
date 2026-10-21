<script lang="ts">
  import { untrack } from 'svelte';
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import JobPipeline from '#lib/components/job-pipeline.svelte';
  import { noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { createMoves } from '#lib/moves.svelte.js';

  // The job page's pipeline wiring without the rest of the page: the moves every control shares, the integrity filter
  // and view the page binds, and the cursor's row it shows in its side column (here, an output). Render inside
  // with-query-client.svelte.
  let {
    stages,
    applications,
    integrityFilter: initialFilter = { ...noIntegrityFilter },
  }: { stages: WorkflowStage[]; applications: ApplicationSummary[]; integrityFilter?: IntegrityFilter } = $props();

  const moves = createMoves();
  let integrityFilter = $state<IntegrityFilter>(untrack(() => ({ ...initialFilter })));
  let view = $state<'table' | 'board'>('table');
  let activeId = $state<string | null>(null);
  const active = $derived(applications.find((application) => application.id === activeId));
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
  bind:activeId
/>
<output aria-label="Selected candidate">{active?.candidateName ?? ''}</output>
