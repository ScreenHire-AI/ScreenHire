<script lang="ts">
  import { createListApplications } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusMark from '#lib/components/status-mark.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import { findSavedView } from '#lib/saved-views.js';

  // Every application in the organization, newest first; new ones and stage moves arrive live (#lib/realtime.ts).
  const applications = createListApplications();
  const view = $derived(findSavedView(page.url.searchParams.get('view')));
  const now = Date.now();
  const all = $derived(applications.data?.data ?? []);
  const shown = $derived(view ? all.filter((application) => view.matches(application, now)) : all);
  let activeId = $state<string | null>(null);
  let selectedIds = $state<string[]>([]);
</script>

<svelte:head>
  <title>{view?.label ?? 'Candidates'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={view ? [{ label: 'Candidates', href: '/candidates' }, { label: view.label }] : [{ label: 'Candidates' }]} />

<main class="flex flex-col gap-3.5 px-5 py-4.5">
  <div class="flex flex-col gap-1.5">
    <h1 class="flex items-center gap-2.5 font-display text-[30px] leading-tight font-bold tracking-[-0.02em]">
      {#if view}<StatusMark shape={view.shape} tone={view.tone} class="size-2.5" />{/if}
      {view?.label ?? 'Candidates'}
    </h1>
    <p class="font-mono text-[11.5px] text-muted-foreground uppercase">
      {#if applications.isSuccess}
        {shown.length} {shown.length === 1 ? 'application' : 'applications'}{#if view} · <a href="/candidates" class="normal-case">show all</a>{/if}
      {:else}&nbsp;{/if}
    </p>
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
          bind:activeId
          bind:selectedIds
          emptyText={view ? 'No candidates in this view.' : 'No candidates yet. Applications appear here as they arrive.'}
        />
      {/if}
    </section>
  {/if}
</main>
