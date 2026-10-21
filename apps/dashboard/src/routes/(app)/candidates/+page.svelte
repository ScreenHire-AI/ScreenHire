<script lang="ts">
  import { createListApplications } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import { clock } from '#lib/clock.svelte.js';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import IntegrityFilterMenu from '#lib/components/integrity-filter.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusMark from '#lib/components/status-mark.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { integrityColumns } from '#lib/integrity-columns.js';
  import { matchesIntegrityFilter, noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { describeError } from '#lib/query.js';
  import { findSavedView } from '#lib/saved-views.js';

  // Every application in the organization, newest first; new ones, stage moves and integrity levels arrive live
  // (#lib/realtime.ts).
  const applications = createListApplications();
  const view = $derived(findSavedView(page.url.searchParams.get('view')));
  const all = $derived(applications.data?.data ?? []);
  // The sidebar counts use the same clock, so a view and its count agree.
  let integrityFilter = $state<IntegrityFilter>({ ...noIntegrityFilter });
  const shown = $derived(
    all.filter((application) => (!view || view.matches(application, clock.now)) && matchesIntegrityFilter(application, integrityFilter)),
  );
  const filtering = $derived(Boolean(view || integrityFilter.authenticity || integrityFilter.aiAssistance));
  let activeId = $state<string | null>(null);
  let selectedIds = $state<string[]>([]);
</script>

<svelte:head>
  <title>{view?.label ?? 'Candidates'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={view ? [{ label: 'Candidates', href: '/candidates' }, { label: view.label }] : [{ label: 'Candidates' }]} />

<main class="flex flex-col gap-3.5 px-5 py-4.5">
  <div class="flex flex-wrap items-end justify-between gap-3">
    <div class="flex flex-col gap-1.5">
      <h1 class="flex items-center gap-2.5 font-display text-page-title">
        {#if view?.mark}<StatusMark shape={view.mark.shape} tone={view.mark.tone} class="size-2.5" />{/if}
        {view?.label ?? 'Candidates'}
      </h1>
      <p class="text-caption text-muted-foreground">
        {#if applications.isSuccess}
          {shown.length} {shown.length === 1 ? 'application' : 'applications'}{#if view} · <a href="/candidates">show all</a>{/if}
        {:else}&nbsp;{/if}
      </p>
    </div>
    <IntegrityFilterMenu bind:filter={integrityFilter} />
  </div>

  {#if applications.isError}
    <QueryError message={describeError(applications.error, 'Could not load candidates')} onretry={() => applications.refetch()} />
  {:else}
    <section aria-label="Candidates" class="overflow-hidden rounded-xl border border-border bg-card">
      {#if applications.isPending}
        <div aria-hidden="true" class="flex flex-col gap-1 p-3">
          {#each [0, 1, 2, 3, 4] as row (row)}<Skeleton class="h-8 w-full" />{/each}
        </div>
      {:else}
        <ApplicationTable
          applications={shown}
          showJob
          extraColumns={integrityColumns}
          bind:activeId
          bind:selectedIds
          emptyText={filtering ? 'No candidates match this view.' : 'No candidates yet. Applications appear here as they arrive.'}
        />
      {/if}
    </section>
  {/if}
</main>
