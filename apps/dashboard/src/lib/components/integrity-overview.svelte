<script lang="ts">
  import type { ApplicationSummary, AuthenticityLevel } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { authenticityLevels, authenticityOrder, integrityOverview, pendingLevel, type IntegrityFilter } from '#lib/integrity.js';

  // A job's authenticity at a glance (design-system section 1): a panel with the applicant count, a numeric legend whose
  // entries are toggles that filter the candidate table, and a stacked bar. The bar is decoration for sighted readers;
  // the legend carries the numbers.
  let {
    applications,
    filter = $bindable(),
  }: {
    applications: readonly ApplicationSummary[];
    filter: IntegrityFilter;
  } = $props();

  const overview = $derived(integrityOverview(applications));

  const bars: Record<AuthenticityLevel | 'Pending', string> = {
    NoConcerns: 'bg-status-clear-mark',
    ReviewSuggested: 'bg-status-review-mark',
    VerificationRecommended: 'bg-status-verify-mark',
    Pending: 'bg-input',
  };

  const entries = $derived([
    ...authenticityOrder.map((id) => ({ id, view: authenticityLevels[id], count: overview.levels[id] })),
    ...(overview.pending > 0 ? [{ id: 'Pending' as const, view: pendingLevel, count: overview.pending }] : []),
  ]);

  function toggle(id: AuthenticityLevel | 'Pending') {
    filter = { ...filter, authenticity: filter.authenticity === id ? null : id };
  }
</script>

<Panel title="Authenticity" count={overview.total}>
  <div class="flex flex-col gap-3">
    <!-- One line that scrolls inside the panel rather than wrapping (F5). -->
    <div class="-m-1 overflow-x-auto p-1">
      <div role="group" aria-label="Authenticity levels" class="flex w-max gap-1">
        {#each entries as entry (entry.id)}
          <Toggle size="lg" bind:pressed={() => filter.authenticity === entry.id, () => toggle(entry.id)}>
            <StatusMark shape={entry.view.shape} tone={entry.view.tone} />
            {entry.view.label}
            <Badge variant="count">{entry.count}</Badge>
          </Toggle>
        {/each}
      </div>
    </div>
    <div aria-hidden="true" class="flex h-2 gap-0.5 overflow-hidden rounded-full bg-secondary">
      {#each entries as entry (entry.id)}
        {#if entry.count > 0}<span data-bar={entry.id} class={bars[entry.id]} style:flex-grow={entry.count}></span>{/if}
      {/each}
    </div>
    {#if overview.dismissed > 0}
      <p class="text-caption text-muted-foreground">{overview.dismissed} dismissed after review</p>
    {/if}
  </div>
</Panel>
