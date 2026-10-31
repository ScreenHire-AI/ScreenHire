<script lang="ts">
  import { untrack } from 'svelte';
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import JobPipeline from '#lib/components/job-pipeline.svelte';
  import { noVerificationFilter, type VerificationFilter } from '#lib/verification.js';
  import { createMoves } from '#lib/moves.svelte.js';

  // The job page's pipeline wiring without the rest of the page: the moves every control shares and the verification filter
  // and view the page binds. Render inside with-query-client.svelte.
  let {
    stages,
    applications,
    verificationFilter: initialFilter = { ...noVerificationFilter },
  }: { stages: WorkflowStage[]; applications: ApplicationSummary[]; verificationFilter?: VerificationFilter } = $props();

  const moves = createMoves();
  let verificationFilter = $state<VerificationFilter>(untrack(() => ({ ...initialFilter })));
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
  bind:verificationFilter
  bind:view
/>
