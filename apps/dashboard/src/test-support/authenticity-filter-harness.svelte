<script lang="ts">
  import type { ApplicationSummary } from '@screenhire/api-client';
  import AuthenticityFilter from '#lib/components/authenticity-filter.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';

  // The chips bound to a filter, as the job's pipeline holds it, for component tests; the filter shows in an output.
  let { applications, menu = false }: { applications: readonly ApplicationSummary[]; menu?: boolean } = $props();
  let filter = $state<IntegrityFilter>({ ...noIntegrityFilter });
</script>

<Tooltip.Provider>
  <AuthenticityFilter {applications} {filter} onchange={(next) => (filter = next)} {menu} />
  <output data-testid="filter">{JSON.stringify(filter)}</output>
</Tooltip.Provider>
