<script lang="ts">
  import type { ApplicationSummary, AuthenticityLevel } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { authenticityLevels, authenticityOrder, integrityOverview, pendingLevel, type IntegrityFilter } from '#lib/integrity.js';

  // A job's authenticity at a glance (design-system section 1): a panel with the applicant count and a toggle per level
  // with its count, which filters the candidate table. No colour-only bar: a status colour comes with its icon and label
  // (T1), and section 22 makes the levels filter chips with counts.
  let {
    applications,
    filter = $bindable(),
  }: {
    applications: readonly ApplicationSummary[];
    filter: IntegrityFilter;
  } = $props();

  const overview = $derived(integrityOverview(applications));

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
    <!-- One line that scrolls inside the panel rather than wrapping (F5); in a narrow panel the levels stack, so the one
         that needs attention is never out of sight (section 11, Toggle). -->
    <div class="@container -m-1 overflow-x-auto p-1">
      <div role="group" aria-label="Authenticity levels" class="grid justify-items-start gap-1 @lg:flex @lg:w-max">
        {#each entries as entry (entry.id)}
          <Toggle size="lg" bind:pressed={() => filter.authenticity === entry.id, () => toggle(entry.id)}>
            {entry.view.label}
            <Badge variant="count">{entry.count}</Badge>
          </Toggle>
        {/each}
      </div>
    </div>
    {#if overview.dismissed > 0}
      <p class="text-caption text-muted-foreground">{overview.dismissed} dismissed after review</p>
    {/if}
  </div>
</Panel>
