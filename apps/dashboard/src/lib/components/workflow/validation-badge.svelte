<script lang="ts">
  import type { DefinitionError } from '@screenhire/api-client';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The API validator's result as the status beside a workflow's title: a badge only when there is a problem (Needs
  // fixes), and nothing when the workflow is fine. The region is busy while the check of the latest change is on its way,
  // so neither assistive technology nor the end-to-end tests read the previous result as the answer. The badge is status
  // only (principle 4: badges are never clickable); where the problems can be fixed, a Fix button beside it brings the
  // focus to the first one. The Test tab shows its own notice with the fix inline, so the button is left out there.
  let {
    errors,
    checking = false,
    editable = true,
    fix = true,
  }: { errors: readonly DefinitionError[]; checking?: boolean; editable?: boolean; fix?: boolean } = $props();
  const things = $derived(`${errors.length} ${errors.length === 1 ? 'thing' : 'things'}`);

  function showProblems() {
    const first = document.querySelector<HTMLElement>('[data-problem-fix]');
    first?.scrollIntoView({ block: 'center' });
    first?.focus({ preventScroll: true });
  }
</script>

<section aria-label="Validation" aria-live="polite" aria-busy={checking} class="flex">
  {#if errors.length > 0}<StatusBadge status="needsFixes" label="Needs fixes" />{/if}
</section>
{#if editable && fix && errors.length > 0}
  <Button variant="outline" size="sm" onclick={showProblems}>Fix {things}</Button>
{/if}
