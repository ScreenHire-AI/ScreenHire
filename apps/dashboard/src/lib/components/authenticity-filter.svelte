<script lang="ts">
  import type { ApplicationSummary, AuthenticityLevel } from '@screenhire/api-client';
  import FilterMenu from '#lib/components/filter-menu.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { verificationFilterOptions, verificationOverview, type VerificationFilter } from '#lib/verification.js';

  // A job's verification states as filter chips with counts (design system section 21): one toggle per state, which
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

  const entries = $derived(
    verificationFilterOptions
      .map((option) => ({ id: option.value, word: option.word, count: option.value === 'Pending' ? overview.pending : overview.levels[option.value] }))
      .filter((entry) => entry.id !== 'Pending' || entry.count > 0),
  );

  function choose(id: AuthenticityLevel | 'Pending' | null) {
    onchange({ ...filter, authenticity: filter.authenticity === id ? null : id });
  }
</script>

{#if menu}
  <FilterMenu
    label="Verification"
    total={overview.total}
    options={entries.map((entry) => ({ value: entry.id, label: entry.word, count: entry.count }))}
    value={filter.authenticity}
    onchange={(value) => onchange({ ...filter, authenticity: value as AuthenticityLevel | 'Pending' | null })}
  />
{:else}
  <div role="group" aria-label="Verification" class="flex shrink-0 items-center gap-1">
    {#each entries as entry (entry.id)}
      <Toggle size="lg" bind:pressed={() => filter.authenticity === entry.id, () => choose(entry.id)}>
        {entry.word}
        <Badge variant="count">{entry.count}</Badge>
      </Toggle>
    {/each}
  </div>
  {#if overview.dismissed > 0}
    <span class="shrink-0 text-caption whitespace-nowrap text-muted-foreground">{overview.dismissed} dismissed after review</span>
  {/if}
{/if}
