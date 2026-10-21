<script lang="ts">
  import { createListApplications } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import { matchesSearch } from '#lib/candidate-table.js';
  import { clock } from '#lib/clock.svelte.js';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IntegrityFilterMenu from '#lib/components/integrity-filter.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { integrityColumns } from '#lib/integrity-columns.js';
  import { isIntegrityFiltered, matchesIntegrityFilter, noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { describeError } from '#lib/query.js';
  import { findSavedView } from '#lib/saved-views.js';

  // Every application in the organization, newest first; new ones, stage moves and integrity levels arrive live
  // (#lib/realtime.ts).
  const applications = createListApplications();
  const view = $derived(findSavedView(page.url.searchParams.get('view')));
  const all = $derived(applications.data?.data ?? []);
  // The sidebar counts use the same clock, so a view and its count agree.
  let integrityFilter = $state<IntegrityFilter>({ ...noIntegrityFilter });
  let search = $state('');
  const shown = $derived(
    all.filter(
      (application) =>
        (!view || view.matches(application, clock.now)) && matchesIntegrityFilter(application, integrityFilter) && matchesSearch(application, search, true),
    ),
  );
  // The saved view is the page's address; the empty state's Clear filters resets only what this page set.
  const narrowed = $derived(isIntegrityFiltered(integrityFilter) || Boolean(search.trim()));
  const filtering = $derived(Boolean(view) || narrowed);
  let activeId = $state<string | null>(null);
  let selectedIds = $state<string[]>([]);
  // The table has no footer (design system K1), so a pill in the panel header says how many of the shown rows are ticked.
  const selectedCount = $derived(shown.filter((application) => selectedIds.includes(application.id)).length);
</script>

<svelte:head>
  <title>{view?.label ?? 'Candidates'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={view ? [{ label: 'Candidates', href: '/candidates' }, { label: view.label }] : [{ label: 'Candidates' }]} />

<main class="flex min-w-0 flex-col">
  <HeaderBand
    title={view?.label ?? 'Candidates'}
    count={applications.isSuccess ? shown.length : undefined}
    countLabel={shown.length === 1 ? 'application' : 'applications'}
    wide
  />

  <div class="@container">
    <div class="mx-auto flex w-full max-w-page flex-col px-4 py-6 @3xl:px-6 2xl:max-w-page-wide">
      <Panel label="Applications" flush>
        {#snippet tools()}
          <SearchField bind:value={search} label="Search candidates" class="flex-[0_1_15rem] @max-3xl/panel:flex-1" />
          <IntegrityFilterMenu bind:filter={integrityFilter} />
          {#if selectedCount > 0}<Badge variant="muted">{selectedCount} selected</Badge>{/if}
        {/snippet}
        {#if applications.isError}
          <div class="p-4">
            <QueryError message={describeError(applications.error, 'Could not load candidates')} onretry={() => applications.refetch()} inset />
          </div>
        {:else if applications.isPending}
          <div aria-hidden="true" class="flex flex-col gap-1 p-3">
            {#each [0, 1, 2, 3, 4] as row (row)}<Skeleton class="h-12 w-full max-md:h-24" />{/each}
          </div>
        {:else}
          <ApplicationTable
            applications={shown}
            showJob
            extraColumns={integrityColumns}
            bind:activeId
            bind:selectedIds
            emptyText={filtering ? 'No candidates match this view.' : 'No candidates yet. Applications appear here as they arrive.'}
            onclearfilters={narrowed ? () => ((search = ''), (integrityFilter = { ...noIntegrityFilter })) : undefined}
          />
        {/if}
      </Panel>
    </div>
  </div>
</main>
