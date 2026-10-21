<script lang="ts">
  import type { ApplicationSummary } from '@screenhire/api-client';
  import IntegrityOverview from '#lib/components/integrity-overview.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';

  // The overview bound to a filter, as the job page holds it, for component tests; the filter shows in an output.
  let { applications }: { applications: readonly ApplicationSummary[] } = $props();
  let filter = $state<IntegrityFilter>({ ...noIntegrityFilter });
</script>

<Tooltip.Provider>
  <IntegrityOverview {applications} bind:filter />
  <output data-testid="filter">{JSON.stringify(filter)}</output>
</Tooltip.Provider>
