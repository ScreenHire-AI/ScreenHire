<script lang="ts">
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import SquareKanbanIcon from '@lucide/svelte/icons/square-kanban';
  import Table2Icon from '@lucide/svelte/icons/table-2';
  import WorkflowIcon from '@lucide/svelte/icons/workflow';
  import { matchesSearch } from '#lib/candidate-table.js';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import IntegrityFilterMenu from '#lib/components/integrity-filter.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PipelineBoard from '#lib/components/pipeline-board.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Kbd } from '#lib/components/ui/kbd/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { initials } from '#lib/format.js';
  import { integrityColumns } from '#lib/integrity-columns.js';
  import { bulkMoveStages, chipOf, matchesIntegrityFilter, noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { createMoves } from '#lib/moves.svelte.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { describeError } from '#lib/query.js';
  import { hasModifier, isTypingTarget } from '#lib/shortcuts.js';
  import { cn } from '#lib/utils.js';

  // A job's candidates as a table or a board (design system section 7, the list template inside the job page), with the
  // selected candidate beside the table. Search and filters sit in the panel header; the page filters nothing itself.
  let {
    jobTitle,
    stages,
    applications,
    pending,
    error,
    onretry,
    integrityFilter = $bindable({ ...noIntegrityFilter }),
  }: {
    jobTitle: string;
    stages: readonly WorkflowStage[];
    applications: readonly ApplicationSummary[];
    pending: boolean;
    /** The applications query's error, or null. */
    error: unknown;
    onretry: () => unknown;
    /** Shared with the job's integrity overview, whose legend sets it through `setIntegrityFilter`. */
    integrityFilter?: IntegrityFilter;
  } = $props();

  const moves = createMoves();

  let stageFilter = $state<string | null>(null);
  let search = $state('');
  let view = $state<'table' | 'board'>('table');
  let activeId = $state<string | null>(null);
  let selectedIds = $state<string[]>([]);

  const searched = $derived(applications.filter((application) => matchesSearch(application, search)));
  const shown = $derived(
    searched.filter((application) => (!stageFilter || application.stage.id === stageFilter) && matchesIntegrityFilter(application, integrityFilter)),
  );
  const filtering = $derived(Boolean(stageFilter) || Boolean(search.trim()) || Boolean(integrityFilter.authenticity || integrityFilter.aiAssistance));
  const moveTargets = $derived(bulkMoveStages(stages, integrityFilter));
  const active = $derived(applications.find((application) => application.id === activeId));
  // Only the rows on screen: a ticked row that a search or filter hides is never moved with the rest.
  const selected = $derived(shown.filter((application) => selectedIds.includes(application.id)));
  const countIn = (stageId: string) => applications.filter((application) => application.stage.id === stageId).length;
  const stageOptions = $derived([
    { id: null, name: 'All', count: applications.length },
    ...stages.map((stage) => ({ id: stage.id, name: stage.name, count: countIn(stage.id) })),
  ]);
  // The stage menu's label is visually hidden in a narrow panel header; only then does its tooltip say it.
  let stageLabelWidth = $state(0);
  const stageSummary = $derived(`Stage: ${stageOptions.find((option) => option.id === stageFilter)?.name ?? 'All'}`);

  /**
   * A new integrity filter starts a new selection, so rows picked under one filter are never moved under another
   * filter's menu (bulkMoveStages, ADR-0004). The filter applies to the table, so it shows the table.
   */
  export function setIntegrityFilter(next: IntegrityFilter) {
    integrityFilter = next;
    selectedIds = [];
    view = 'table';
  }

  function clearFilters() {
    stageFilter = null;
    search = '';
    setIntegrityFilter({ ...noIntegrityFilter });
  }

  // V switches between the table and the board while focus is in the pipeline.
  function onkeydown(event: KeyboardEvent) {
    if (!preferences.shortcutsEnabled || hasModifier(event) || isTypingTarget(event.target)) return;
    if (event.key.toLowerCase() === 'v') {
      event.preventDefault();
      view = view === 'table' ? 'board' : 'table';
    }
  }

  async function moveSelected(stage: WorkflowStage) {
    // The ones that failed stay selected, so they can be moved again.
    selectedIds = await moves.moveAll(selected, stage);
  }

  const views = [
    { id: 'table', label: 'Table', icon: Table2Icon },
    { id: 'board', label: 'Board', icon: SquareKanbanIcon },
  ] as const;
</script>

<div class="flex flex-wrap items-start gap-5">
  <Panel label="Pipeline" flush class="flex-[999_1_520px]" {onkeydown}>
    {#snippet tools()}
      <SearchField bind:value={search} label="Search candidates" class="flex-[0_1_15rem] @max-3xl/panel:flex-1" />
      {#if view === 'table'}
        <DropdownMenu.Root>
          <Tooltip.Root disabled={stageLabelWidth > 1}>
            <Tooltip.Trigger>
              {#snippet child({ props })}
                <DropdownMenu.Trigger
                  {...props}
                  class={cn(
                    buttonVariants({ variant: 'outline' }),
                    'min-w-0 @max-3xl/panel:w-8 @max-3xl/panel:px-0 pointer-coarse:@max-3xl/panel:w-11',
                    stageFilter && 'border-primary text-primary',
                  )}
                >
                  <WorkflowIcon aria-hidden="true" />
                  <span bind:clientWidth={stageLabelWidth} class="max-w-40 truncate @max-3xl/panel:sr-only">{stageSummary}</span>
                </DropdownMenu.Trigger>
              {/snippet}
            </Tooltip.Trigger>
            <Tooltip.Content>{stageSummary}</Tooltip.Content>
          </Tooltip.Root>
          <DropdownMenu.Content align="start" class="w-56">
            <DropdownMenu.RadioGroup value={stageFilter ?? 'all'} onValueChange={(value) => (stageFilter = value === 'all' ? null : value)}>
              {#each stageOptions as option (option.id ?? 'all')}
                <DropdownMenu.RadioItem value={option.id ?? 'all'}>
                  {option.name}
                  <Badge variant="count" class="ml-auto">{option.count}</Badge>
                </DropdownMenu.RadioItem>
              {/each}
            </DropdownMenu.RadioGroup>
          </DropdownMenu.Content>
        </DropdownMenu.Root>
        <IntegrityFilterMenu bind:filter={() => integrityFilter, setIntegrityFilter} />
        {#if selected.length > 0}
          <DropdownMenu.Root>
            <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline' }), 'shrink-0')}>
              Move {selected.length} selected…
            </DropdownMenu.Trigger>
            <DropdownMenu.Content align="end" class="w-52">
              {#each moveTargets as stage (stage.id)}
                <DropdownMenu.Item onSelect={() => moveSelected(stage)}>{stage.name}</DropdownMenu.Item>
              {/each}
            </DropdownMenu.Content>
          </DropdownMenu.Root>
        {/if}
      {/if}
    {/snippet}
    {#snippet actions()}
      <div role="group" aria-label="View" class="flex gap-0.5 rounded-lg bg-secondary p-0.5">
        {#each views as option (option.id)}
          {@const Icon = option.icon}
          <Tooltip.Root>
            <Tooltip.Trigger>
              {#snippet child({ props })}
                <button
                  {...props}
                  type="button"
                  aria-pressed={view === option.id}
                  aria-keyshortcuts={preferences.shortcutsEnabled ? 'V' : undefined}
                  onclick={() => (view = option.id)}
                  class={[
                    'flex h-7 items-center gap-1.5 rounded-md px-2.5 text-body pointer-coarse:h-11 @max-3xl/panel:px-1.5 pointer-coarse:@max-3xl/panel:w-11 pointer-coarse:@max-3xl/panel:justify-center',
                    view === option.id ? 'bg-card font-semibold text-foreground' : 'text-muted-foreground hover:text-foreground',
                  ]}
                >
                  <Icon aria-hidden="true" />
                  <span class="@max-3xl/panel:sr-only">{option.label}</span>
                </button>
              {/snippet}
            </Tooltip.Trigger>
            <Tooltip.Content>
              {option.label}
              {#if preferences.shortcutsEnabled}<Kbd>V</Kbd>{/if}
            </Tooltip.Content>
          </Tooltip.Root>
        {/each}
      </div>
    {/snippet}

    {#if error}
      <div class="p-4">
        <QueryError message={describeError(error, 'Could not load the candidates')} {onretry} inset />
      </div>
    {:else if pending}
      <div aria-hidden="true" class="flex flex-col gap-1 p-3">
        {#each [0, 1, 2, 3] as row (row)}<Skeleton class="h-12 w-full max-md:h-16" />{/each}
      </div>
    {:else if view === 'table'}
      <ApplicationTable
        applications={shown}
        label={`Candidates for ${jobTitle}`}
        {stages}
        {moves}
        extraColumns={integrityColumns}
        bind:activeId
        bind:selectedIds
        emptyText={filtering ? 'No candidates match these filters.' : 'No candidates yet. Applications appear here as they arrive.'}
        onclearfilters={filtering ? clearFilters : undefined}
      />
    {:else}
      <PipelineBoard applications={searched} {stages} {moves} />
    {/if}
    <p role="status" class={['px-4 py-2 text-body empty:hidden', moves.failure ? 'text-status-verify' : 'text-muted-foreground']}>
      {moves.announcement}
    </p>
  </Panel>

  <!-- The panel keeps its place before a row is chosen: appearing on the first click's mousedown narrowed the table
       under the pointer, so the mouseup missed the candidate's link and the click never opened it. Phones open the
       candidate from its list item instead. -->
  {#if view === 'table' && applications.length > 0}
    <Panel title="Selected candidate" class="flex-[1_1_320px] max-md:hidden lg:max-w-95">
      {#if active}
        {@render selectedCandidate(active)}
      {:else}
        <p class="text-muted-foreground">Choose a candidate to see their stage and checks here.</p>
      {/if}
    </Panel>
  {/if}
</div>

{#snippet selectedCandidate(application: ApplicationSummary)}
  <div class="flex flex-col gap-4">
    <div class="flex min-w-0 items-center gap-3">
      <span aria-hidden="true" class="grid size-10 flex-none place-items-center rounded-full bg-secondary font-bold text-secondary-foreground"
        >{initials(application.candidateName)}</span
      >
      <h3 class="min-w-0 font-display text-panel-title break-words">{application.candidateName}</h3>
    </div>
    <dl class="grid grid-cols-[auto_1fr] items-center gap-x-4 gap-y-2">
      <dt class="text-caption text-muted-foreground">Stage</dt>
      <dd><Badge variant="muted">{application.stage.name}</Badge></dd>
      <dt class="text-caption text-muted-foreground">Entered stage</dt>
      <dd><RelativeTime at={application.stageEnteredAt} /></dd>
      {#each [{ kind: 'authenticity', label: 'Authenticity' }, { kind: 'aiAssistance', label: 'AI assistance' }] as const as row (row.kind)}
        {@const chip = chipOf(application.integrity, row.kind)}
        <dt class="text-caption text-muted-foreground">{row.label}</dt>
        <dd><IntegrityChip kind={row.kind} level={chip?.level ?? null} reasons={chip?.reasons ?? 0} dismissed={chip?.dismissed ?? false} /></dd>
      {/each}
    </dl>
    <div class="flex flex-wrap gap-2">
      <a href={`/applications/${application.id}`} class={cn(buttonVariants({ variant: 'outline', size: 'sm' }))}>Open application</a>
      <MoveToStage
        candidateName={application.candidateName}
        currentStageId={application.stage.id}
        {stages}
        disabled={moves.pending}
        onmove={(stage) => moves.move(application, stage)}
      />
    </div>
  </div>
{/snippet}
