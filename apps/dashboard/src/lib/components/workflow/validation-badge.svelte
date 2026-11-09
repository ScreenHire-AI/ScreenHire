<script lang="ts">
  import CircleAlertIcon from '~icons/lucide/circle-alert';
  import type { DefinitionError } from '@screenhire/api-client';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The API validator's result as the status beside a workflow's title. The region is busy while the check of the
  // latest change is on its way, so neither assistive technology nor the end-to-end tests read the previous result as
  // the answer. The problems themselves are listed in a `ProblemsPanel`. A workflow that cannot be edited here has
  // problems, not problems to fix. The badge is status only (principle 4: badges are never clickable); where the problems
  // can be fixed, a Fix button beside it scrolls to the list and focuses its first Fix.
  let {
    errors,
    checking = false,
    editable = true,
  }: { errors: readonly DefinitionError[]; checking?: boolean; editable?: boolean } = $props();
  const problems = $derived(`${errors.length} ${errors.length === 1 ? 'problem' : 'problems'}`);

  function showProblems() {
    const first = document.querySelector<HTMLElement>('[data-problem-fix]');
    first?.scrollIntoView({ block: 'center' });
    first?.focus({ preventScroll: true });
  }
</script>

<section aria-label="Validation" aria-live="polite" aria-busy={checking} class="flex">
  {#if errors.length === 0}
    <StatusBadge status="done" label="No problems found" />
  {:else}
    <StatusBadge status="needsFixes" label={editable ? `${problems} to fix` : problems} />
  {/if}
</section>
{#if editable && errors.length > 0}
  <Button variant="outline" size="sm" onclick={showProblems}><CircleAlertIcon aria-hidden="true" class="text-status-verify-mark" />Fix {problems}</Button>
{/if}
