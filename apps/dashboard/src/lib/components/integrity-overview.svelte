<script lang="ts">
  import type { ApplicationSummary, AuthenticityLevel } from '@screenhire/api-client';
  import StatusMark from '#lib/components/status-mark.svelte';
  import { authenticityLevels, authenticityOrder, integrityOverview, pendingLevel, type IntegrityFilter } from '#lib/integrity.js';

  // A job's authenticity at a glance (design-system section 1): a numeric legend, each entry a toggle that filters the
  // candidate table, and a stacked bar. The bar is decoration for sighted readers; the legend carries the numbers.
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

<section aria-labelledby="integrity-overview-heading" class="flex flex-col gap-2.5 rounded-xl border border-border bg-card px-4 py-3.5">
  <div class="flex flex-wrap items-center gap-x-5 gap-y-2">
    <h2 id="integrity-overview-heading" class="flex items-baseline gap-2 font-normal">
      <span class="font-display text-page-title tabular-nums">{overview.total}</span>
      <span class="text-muted-foreground">{overview.total === 1 ? 'applicant' : 'applicants'} · authenticity</span>
    </h2>
    <ul aria-label="Authenticity levels" class="flex flex-wrap gap-x-1 gap-y-1">
      {#each entries as entry (entry.id)}
        <li>
          <button
            type="button"
            aria-pressed={filter.authenticity === entry.id}
            onclick={() => toggle(entry.id)}
            class={[
              'flex h-7 items-center gap-[7px] rounded-md px-2 hover:bg-secondary',
              filter.authenticity === entry.id && 'bg-secondary ring-1 ring-input',
            ]}
          >
            <StatusMark shape={entry.view.shape} tone={entry.view.tone} class="size-[9px]" />
            <span class="font-bold tabular-nums">{entry.count}</span>
            {entry.view.label}
          </button>
        </li>
      {/each}
    </ul>
    {#if overview.dismissed > 0}
      <span class="text-caption text-muted-foreground">{overview.dismissed} dismissed after review</span>
    {/if}
  </div>
  <div aria-hidden="true" class="flex h-2 gap-0.5 overflow-hidden rounded-full bg-secondary">
    {#each entries as entry (entry.id)}
      {#if entry.count > 0}<span data-bar={entry.id} class={bars[entry.id]} style:flex-grow={entry.count}></span>{/if}
    {/each}
  </div>
</section>
