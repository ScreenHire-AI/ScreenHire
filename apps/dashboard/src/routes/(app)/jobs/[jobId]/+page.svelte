<script lang="ts">
  import type { ApplicationSummary } from '@screenhire/api-client';
  import { ApiError, createCloseJob, createGetJob, createListBoards, createListJobApplications } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import DistributionStrip from '#lib/components/distribution-strip.svelte';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import IntegrityFilterMenu from '#lib/components/integrity-filter.svelte';
  import IntegrityOverview from '#lib/components/integrity-overview.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import PipelineBoard from '#lib/components/pipeline-board.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { age, ago, employmentLabel, formatSalary, formatShortDate, initials, sourceLabel } from '#lib/format.js';
  import { integrityColumns } from '#lib/integrity-columns.js';
  import { bulkMoveStages, chipOf, isIntegrityFiltered, matchesIntegrityFilter, noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { createMoves } from '#lib/moves.svelte.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { hasModifier, isTypingTarget } from '#lib/shortcuts.js';
  import { cn } from '#lib/utils.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  // ApplicationSubmitted, ApplicationMoved, IntegrityAssessed and JobPostingsChanged refetch them live (#lib/realtime.ts).
  const job = createGetJob(() => params.jobId);
  const applications = createListJobApplications(() => params.jobId);
  const boards = createListBoards();
  const moves = createMoves();
  const close = createCloseJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));

  const detail = $derived(job.data?.data);
  const stages = $derived(detail?.workflow.stages ?? []);
  const all = $derived(applications.data?.data ?? []);

  let stageFilter = $state<string | null>(null);
  // Set from the overview's legend or the filter menu, through setIntegrityFilter.
  let integrityFilter = $state<IntegrityFilter>({ ...noIntegrityFilter });
  let view = $state<'table' | 'board'>('table');
  let activeId = $state<string | null>(null);
  let selectedIds = $state<string[]>([]);
  let confirmClose = $state(false);
  let closeFailure = $state('');

  const shown = $derived(
    all.filter((application) => (!stageFilter || application.stage.id === stageFilter) && matchesIntegrityFilter(application, integrityFilter)),
  );
  const filtering = $derived(Boolean(stageFilter) || isIntegrityFiltered(integrityFilter));
  const moveTargets = $derived(bulkMoveStages(stages, integrityFilter));
  const active = $derived(all.find((application) => application.id === activeId));
  const selected = $derived(all.filter((application) => selectedIds.includes(application.id)));
  const countIn = (stageId: string) => all.filter((application) => application.stage.id === stageId).length;

  const meta = $derived.by(() => {
    if (!detail) return [];
    const parts = [detail.location, employmentLabel(detail.employmentType)];
    if (detail.salary) parts.push(formatSalary(detail.salary));
    if (applications.isSuccess) parts.push(`${all.length} ${all.length === 1 ? 'applicant' : 'applicants'}`);
    if (detail.status === 'Open' && detail.publishedAt) parts.push(`Opened ${ago(detail.publishedAt, Date.now())}`);
    else if (detail.status === 'Closed' && detail.closedAt) parts.push(`Closed ${formatShortDate(detail.closedAt)}`);
    else parts.push(detail.status);
    return parts.filter(Boolean);
  });

  // V switches between the table and the board while focus is in the pipeline.
  function onPipelineKeydown(event: KeyboardEvent) {
    if (!preferences.shortcutsEnabled || hasModifier(event) || isTypingTarget(event.target)) return;
    if (event.key.toLowerCase() === 'v') {
      event.preventDefault();
      view = view === 'table' ? 'board' : 'table';
    }
  }

  // A new integrity filter starts a new selection, so rows picked under one filter are never moved under another
  // filter's menu (bulkMoveStages). The filter applies to the table, so it shows the table.
  function setIntegrityFilter(next: IntegrityFilter) {
    integrityFilter = next;
    selectedIds = [];
    view = 'table';
  }

  async function moveSelected(stage: (typeof stages)[number]) {
    // The ones that failed stay selected, so they can be moved again.
    selectedIds = await moves.moveAll(selected, stage);
  }

  async function closeEverywhere() {
    closeFailure = '';
    try {
      await close.mutateAsync({ jobId: params.jobId });
      confirmClose = false;
    } catch (error) {
      closeFailure = describeError(error, 'Could not close the job');
    }
  }

  const notFound = $derived(job.error instanceof ApiError && job.error.status === 404);
</script>

<svelte:head>
  <title>{detail?.title ?? 'Job'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Jobs', href: '/' }, ...(detail?.department ? [{ label: detail.department }] : []), { label: detail?.title ?? 'Job' }]} />

<main class="flex flex-col gap-3.5 px-5 pt-4.5 pb-9">
  {#if job.isError}
    {#if notFound}
      <h1 class="font-display text-[30px] font-bold tracking-[-0.02em]">Job not found</h1>
      <p class="text-muted-foreground">This job does not exist, or it belongs to another organization. <a href="/">Back to jobs</a></p>
    {:else}
      <h1 class="font-display text-[30px] font-bold tracking-[-0.02em]">Job</h1>
      <QueryError message={describeError(job.error, 'Could not load this job')} onretry={() => job.refetch()} />
    {/if}
  {:else if !detail}
    <div aria-hidden="true" class="flex flex-col gap-2">
      <Skeleton class="h-9 w-80" />
      <Skeleton class="h-4 w-96" />
      <Skeleton class="mt-2 h-10 w-full" />
    </div>
    <p class="sr-only" role="status">Loading the job…</p>
  {:else}
    <section aria-label="Job" class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex min-w-0 flex-col gap-1.5">
        <h1 class="font-display text-[30px] leading-[1.1] font-bold tracking-[-0.02em]">{detail.title}</h1>
        <ul aria-label="Job details" class="flex flex-wrap gap-x-3.5 gap-y-1 font-mono text-[11.5px] text-muted-foreground uppercase">
          {#each meta as part (part)}<li>{part}</li>{/each}
        </ul>
      </div>
      <div class="flex flex-wrap gap-2">
        <a href={`/jobs/${detail.id}/workflow`} class={cn(buttonVariants({ variant: 'outline' }), 'h-[34px] font-semibold')}>
          Workflow{#if detail.template?.updateAvailable}<span class="text-primary"> · update available</span>{/if}
        </a>
        {#if detail.status === 'Open'}
          <Button variant="outline" class="h-[34px] border-destructive/40 font-semibold text-destructive" onclick={() => (confirmClose = true)}
            >Close &amp; remove everywhere</Button
          >
        {/if}
      </div>
    </section>

    <DistributionStrip job={detail} boards={boards.data?.data ?? []} />

    {#if applications.isSuccess && all.length > 0}
      <IntegrityOverview applications={all} bind:filter={() => integrityFilter, setIntegrityFilter} />
    {/if}

    <div class="flex flex-wrap items-start gap-3.5">
      <!-- svelte-ignore a11y_no_noninteractive_element_interactions: V is a shortcut for the focusable rows and cards inside, whose key presses bubble here -->
      <section
        aria-label="Pipeline"
        class="min-w-0 flex-[999_1_600px] overflow-hidden rounded-xl border border-border bg-card"
        onkeydown={onPipelineKeydown}
      >
        <div class="flex flex-wrap items-center justify-between gap-2 border-b border-border-subtle px-2.5 py-2">
          {#if view === 'table'}
            <div role="group" aria-label="Stage" class="flex flex-wrap gap-0.5 rounded-[9px] bg-secondary p-[3px]">
              {#each [{ id: null, name: 'All', count: all.length }, ...stages.map((stage) => ({ id: stage.id, name: stage.name, count: countIn(stage.id) }))] as option (option.id ?? 'all')}
                <button
                  type="button"
                  aria-pressed={stageFilter === option.id}
                  onclick={() => (stageFilter = option.id)}
                  class={[
                    'h-7 rounded-[7px] px-2.5',
                    stageFilter === option.id
                      ? 'bg-card font-bold text-foreground shadow-[0_1px_2px_color-mix(in_oklab,var(--foreground)_14%,transparent)]'
                      : 'font-semibold text-secondary-foreground hover:bg-card/60',
                  ]}
                >
                  {option.name}
                  <span class="font-mono text-[11px] font-medium text-muted-foreground tabular-nums">{option.count}</span>
                </button>
              {/each}
            </div>
          {:else}
            <h2 class="px-1 font-bold">Board</h2>
          {/if}
          <div class="flex flex-wrap items-center gap-2">
            {#if view === 'table'}
              <IntegrityFilterMenu bind:filter={() => integrityFilter, setIntegrityFilter} />
            {/if}
            {#if view === 'table' && selected.length > 0}
              <DropdownMenu.Root>
                <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline', size: 'sm' }))}>
                  Move {selected.length} selected…
                </DropdownMenu.Trigger>
                <DropdownMenu.Content align="end" class="w-52">
                  {#each moveTargets as stage (stage.id)}
                    <DropdownMenu.Item onSelect={() => moveSelected(stage)}>{stage.name}</DropdownMenu.Item>
                  {/each}
                </DropdownMenu.Content>
              </DropdownMenu.Root>
            {/if}
            <div role="group" aria-label="View" class="flex gap-0.5 rounded-[9px] bg-secondary p-[3px]">
              {#each [{ id: 'table', label: 'Table' }, { id: 'board', label: 'Board' }] as const as option (option.id)}
                <button
                  type="button"
                  aria-pressed={view === option.id}
                  aria-keyshortcuts={preferences.shortcutsEnabled ? 'V' : undefined}
                  onclick={() => (view = option.id)}
                  class={['h-7 rounded-[7px] px-2.5', view === option.id ? 'bg-card font-bold' : 'font-semibold text-secondary-foreground']}
                  >{option.label}</button
                >
              {/each}
            </div>
          </div>
        </div>

        {#if applications.isError}
          <div class="p-3">
            <QueryError message={describeError(applications.error, 'Could not load the candidates')} onretry={() => applications.refetch()} />
          </div>
        {:else if applications.isPending}
          <div aria-hidden="true" class="flex flex-col gap-1 p-3">
            {#each [0, 1, 2, 3] as row (row)}<Skeleton class="h-8 w-full" />{/each}
          </div>
        {:else if view === 'table'}
          <ApplicationTable
            applications={shown}
            label={`Candidates for ${detail.title}`}
            {stages}
            {moves}
            extraColumns={integrityColumns}
            bind:activeId
            bind:selectedIds
            emptyText={filtering ? 'No candidates match these filters.' : 'No candidates yet. Applications appear here as they arrive.'}
          >
            {#snippet hints()}<span><kbd>V</kbd> board view</span>{/snippet}
          </ApplicationTable>
        {:else}
          <PipelineBoard applications={all} {stages} {moves} />
          <p class="border-t border-border-subtle bg-muted px-3 py-2 font-mono text-[11px] text-muted-foreground">
            <kbd>Shift</kbd> + <kbd>←</kbd> / <kbd>→</kbd> move a card · <kbd>Space</kbd> pick up · <kbd>Enter</kbd> open
            {#if preferences.shortcutsEnabled}· <kbd>V</kbd> table view{/if}
          </p>
        {/if}
        <p role="status" class={['px-3 py-1.5 text-[12.5px] empty:hidden', moves.failure ? 'text-status-verify' : 'text-muted-foreground']}>
          {moves.announcement}
        </p>
      </section>

      <!-- The panel keeps its place before a row is chosen: appearing on the first click's mousedown narrowed the table
           under the pointer, so the mouseup missed the candidate's link and the click never opened it. -->
      {#if view === 'table' && all.length > 0}
        {#if active}
          {@render panel(active)}
        {:else}
          <aside aria-label="Selected candidate" class="flex min-w-0 flex-[1_1_320px] flex-col rounded-xl border border-border bg-card">
            <p class="px-4 py-3.5 text-[13px] text-muted-foreground">Choose a candidate to see their stage and checks here.</p>
          </aside>
        {/if}
      {/if}
    </div>
  {/if}
</main>

{#snippet panel(application: ApplicationSummary)}
  <aside aria-label="Selected candidate" class="flex min-w-0 flex-[1_1_320px] flex-col rounded-xl border border-border bg-card">
    <div class="flex items-center gap-2.5 border-b border-border-subtle px-4 py-3.5">
      <span aria-hidden="true" class="grid size-10 flex-none place-items-center rounded-full bg-secondary font-bold text-secondary-foreground"
        >{initials(application.candidateName)}</span
      >
      <div class="flex min-w-0 flex-col">
        <h2 class="font-display text-[19px] font-bold">{application.candidateName}</h2>
        <span class="text-[12.5px] text-muted-foreground">
          Applied {ago(application.submittedAt, Date.now())} via {sourceLabel(application.source)}
        </span>
      </div>
    </div>
    <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 border-b border-border-subtle px-4 py-3.5">
      <dt class="font-mono text-[10.5px] tracking-[0.06em] text-muted-foreground uppercase">Stage</dt>
      <dd>{application.stage.name}</dd>
      <dt class="font-mono text-[10.5px] tracking-[0.06em] text-muted-foreground uppercase">In stage</dt>
      <dd>{age(application.stageEnteredAt, Date.now())}</dd>
      {#each [{ kind: 'authenticity', label: 'Authenticity' }, { kind: 'aiAssistance', label: 'AI assist' }] as const as row (row.kind)}
        {@const chip = chipOf(application.integrity, row.kind)}
        <dt class="font-mono text-[10.5px] tracking-[0.06em] text-muted-foreground uppercase">{row.label}</dt>
        <dd><IntegrityChip kind={row.kind} level={chip?.level ?? null} reasons={chip?.reasons ?? 0} dismissed={chip?.dismissed ?? false} /></dd>
      {/each}
    </dl>
    <div class="flex flex-wrap gap-1.5 px-4 py-3.5">
      <a href={`/applications/${application.id}`} class={cn(buttonVariants({ size: 'sm' }))}>Open application</a>
      <MoveToStage
        candidateName={application.candidateName}
        currentStageId={application.stage.id}
        {stages}
        disabled={moves.pending}
        onmove={(stage) => moves.move(application, stage)}
      />
    </div>
  </aside>
{/snippet}

<Dialog.Root bind:open={confirmClose}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Close this job and remove it everywhere?</Dialog.Title>
      <Dialog.Description>
        The careers page stops taking applications at once, and every board is asked to remove the job. Boards that
        refresh on their own schedule may show it until they do. A closed job cannot be reopened.
      </Dialog.Description>
    </Dialog.Header>
    <p role="status" class="text-status-verify empty:hidden">{closeFailure}</p>
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline' }))}>Keep it open</Dialog.Close>
      <Button variant="destructive" disabled={close.isPending} onclick={closeEverywhere}>Close &amp; remove everywhere</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
