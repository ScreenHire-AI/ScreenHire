<script lang="ts">
  import type { ApplicationSummary, AuthenticityLevel } from '@screenhire/api-client';
  import FilterMenu from '#lib/components/filter-menu.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { authenticityLevels, authenticityOrder, verificationOverview, pendingLevel, type VerificationFilter } from '#lib/verification.js';

  // A job's authenticity levels as filter chips with counts (design system section 21): one toggle per level, which
  // filters the candidates, and a second click clears it. The chips carry words and counts only: status icons belong in
  // status badges (I2), and a status colour never comes without them (T1). On a phone, where the chips do not fit, the
  // same choices sit in a filter menu.
  let {
    applications,
    filter,
    onchange,
    menu = false,
  }: {
    applications: readonly ApplicationSummary[];
    filter: VerificationFilter;
    /** Called with the new filter; the page decides what a new filter resets (the selection, ADR-0004). */
    onchange: (next: VerificationFilter) => void;
    /** A filter menu instead of chips, for a phone. */
    menu?: boolean;
  } = $props();

  const overview = $derived(verificationOverview(applications));

  const entries = $derived([
    ...authenticityOrder.map((id) => ({ id, view: authenticityLevels[id], count: overview.levels[id] })),
    ...(overview.pending > 0 ? [{ id: 'Pending' as const, view: pendingLevel, count: overview.pending }] : []),
  ]);

  function choose(id: AuthenticityLevel | 'Pending' | null) {
    onchange({ ...filter, authenticity: filter.authenticity === id ? null : id });
  }
</script>

{#if menu}
  <FilterMenu
    label="Authenticity"
    total={overview.total}
    options={entries.map((entry) => ({ value: entry.id, label: entry.view.label, count: entry.count }))}
    value={filter.authenticity}
    onchange={(value) => onchange({ ...filter, authenticity: value as AuthenticityLevel | 'Pending' | null })}
  />
{:else}
  <div role="group" aria-label="Authenticity" class="flex shrink-0 items-center gap-1">
    {#each entries as entry (entry.id)}
      <Toggle size="lg" bind:pressed={() => filter.authenticity === entry.id, () => choose(entry.id)}>
        {entry.view.label}
        <Badge variant="count">{entry.count}</Badge>
      </Toggle>
    {/each}
  </div>
  {#if overview.dismissed > 0}
    <span class="shrink-0 text-caption whitespace-nowrap text-muted-foreground">{overview.dismissed} dismissed after review</span>
  {/if}
{/if}
