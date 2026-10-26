<script lang="ts">
  import type { DefinitionError } from '@screenhire/api-client';
  import StatusBadge from '#lib/components/status-badge.svelte';

  // The API validator's result as the status beside a workflow's title. The region is busy while the check of the
  // latest change is on its way, so neither assistive technology nor the end-to-end tests read the previous result as
  // the answer. The problems themselves are listed in a `ProblemsPanel`. A workflow that cannot be edited here has
  // problems, not problems to fix.
  let {
    errors,
    checking = false,
    editable = true,
  }: { errors: readonly DefinitionError[]; checking?: boolean; editable?: boolean } = $props();
  const problems = $derived(`${errors.length} ${errors.length === 1 ? 'problem' : 'problems'}`);
</script>

<section aria-label="Validation" aria-live="polite" aria-busy={checking} class="flex">
  {#if errors.length === 0}
    <StatusBadge status="done" label="No problems found" />
  {:else}
    <StatusBadge status="needsFixes" label={editable ? `${problems} to fix` : problems} />
  {/if}
</section>
