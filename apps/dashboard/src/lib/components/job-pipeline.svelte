<script lang="ts">
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import { can } from '#lib/permissions.js';
  import { matchesSearch } from '#lib/candidate-table.js';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import AuthenticityFilter from '#lib/components/authenticity-filter.svelte';
  import EmailComposerSheet from '#lib/components/email-composer-sheet.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import FilterMenu from '#lib/components/filter-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PipelineBoard from '#lib/components/pipeline-board.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SelectionBar from '#lib/components/selection-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Kbd } from '#lib/components/ui/kbd/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { integrityColumns } from '#lib/integrity-columns.js';
  import { ratingColumn } from '#lib/rating-column.js';
  import {
    bulkMoveStages,
    isIntegrityFiltered,
    matchesIntegrityFilter,
    noIntegrityFilter,
    type IntegrityFilter,
  } from '#lib/integrity.js';
  import type { Moves } from '#lib/moves.svelte.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { describeError } from '#lib/query.js';
  import { hasModifier, isTypingTarget } from '#lib/shortcuts.js';

  // A job's candidates as a table or a board, the job page's work area (design system section 21). The panel header holds
  // the search and the view switch; the row under it holds the Stage filter and the authenticity chips, one line that
  // scrolls inside the panel (F5). Ticked rows get the selection bar. On a phone the filters are menus beside the Sort
  // chip. The page filters nothing itself.
  let {
    jobTitle,
    stages,
    applications,
    pending,
    error,
    onretry,
    moves,
    integrityFilter = $bindable({ ...noIntegrityFilter }),
    view = $bindable('table'),
  }: {
    jobTitle: string;
    stages: readonly WorkflowStage[];
    applications: readonly ApplicationSummary[];
    pending: boolean;
    /** The applications query's error, or null. */
    error: unknown;
    onretry: () => unknown;
    moves: Moves;
    integrityFilter?: IntegrityFilter;
    view?: 'table' | 'board';
  } = $props();

  // The sidebar's own phone test (under md), where the search moves out of the header and the filters become menus.
  const sidebar = Sidebar.useSidebar();

  let stageFilter = $state<string | null>(null);
  let search = $state('');
  let selectedIds = $state<string[]>([]);

  const searched = $derived(applications.filter((application) => matchesSearch(application, search)));
  const onBoard = $derived(searched.filter((application) => matchesIntegrityFilter(application, integrityFilter)));
  // A stage taken out of the workflow on its own page filters nothing, and the menu reads All again.
  const chosenStage = $derived(stages.some((stage) => stage.id === stageFilter) ? stageFilter : null);
  const shown = $derived(onBoard.filter((application) => !chosenStage || application.stage.id === chosenStage));
  const filtering = $derived(Boolean(chosenStage) || Boolean(search.trim()) || isIntegrityFiltered(integrityFilter));
  const moveTargets = $derived(bulkMoveStages(stages, integrityFilter));
  // Only the rows on screen: a ticked row that a search or filter hides is never moved with the rest.
  const selected = $derived(shown.filter((application) => selectedIds.includes(application.id)));
  // Email to the selection, hidden under an integrity filter for the reason bulkMoveStages gives (ADR-0004).
  const emailTemplates = createListEmailTemplates();
  const canEmail = $derived(Boolean(emailTemplates.data?.data.sendingEnabled) && can(page.data.user, 'EmailCandidates'));
  let emailOpen = $state(false);
  let emailed = $state('');
  const stageOptions = $derived(
    stages.map((stage) => ({ value: stage.id, label: stage.name, count: applications.filter((application) => application.stage.id === stage.id).length })),
  );
  const views = [
    { id: 'table', label: 'Table' },
    { id: 'board', label: 'Board' },
  ] as const;

  /**
   * A new integrity filter starts a new selection, so rows picked under one filter are never moved under another
   * filter's menu (bulkMoveStages, ADR-0004).
   */
  function setIntegrityFilter(next: IntegrityFilter) {
    integrityFilter = next;
    selectedIds = [];
  }

  // Keeps the view: the board's empty state clears its search here, and stays on the board.
  function clearFilters() {
    stageFilter = null;
    search = '';
    integrityFilter = { ...noIntegrityFilter };
    selectedIds = [];
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
    // The ones that failed stay selected, so they can be moved again, and so do the ticked rows a filter hides.
    const moving = selected.map((application) => application.id);
    const failed = await moves.moveAll(selected, stage);
    selectedIds = [...selectedIds.filter((id) => !moving.includes(id)), ...failed];
  }
</script>

<!-- V is a shortcut for the focusable rows and cards inside, whose key presses bubble to the panel. On a phone the
     header has no room for a usable search beside the view switch, so the search takes its own row under it (F5). The
     board fills the panel, which fills the page's height, so it scrolls inside itself. -->
<Panel title="Pipeline" flush fill={!sidebar.isMobile} class={sidebar.isMobile ? undefined : 'min-h-0 flex-1'} {onkeydown}>
  {#snippet tools()}
    {#if !sidebar.isMobile}{@render searchField('flex-[0_1_15rem] @max-3xl/panel:flex-1')}{/if}
  {/snippet}
  {#snippet actions()}
    <div role="group" aria-label="View" class="flex gap-1">
      {#each views as option (option.id)}
        <Tooltip.Root>
          <Tooltip.Trigger>
            {#snippet child({ props })}
              <Toggle
                {...props}
                bind:pressed={() => view === option.id, () => (view = option.id)}
                aria-keyshortcuts={preferences.shortcutsEnabled ? 'V' : undefined}>{option.label}</Toggle
              >
            {/snippet}
          </Tooltip.Trigger>
          <Tooltip.Content>
            Switch view{#if preferences.shortcutsEnabled}<Kbd>V</Kbd>{/if}
          </Tooltip.Content>
        </Tooltip.Root>
      {/each}
    </div>
  {/snippet}

  {#if sidebar.isMobile}
    <div class="flex border-b border-border-subtle px-4 py-2">{@render searchField('flex-1')}</div>
  {/if}
  {#if !pending && !error}
    {#if !sidebar.isMobile}
      <!-- The filters: one line that scrolls inside the panel rather than wrapping (F5). The board's columns are the
           stages, so only the table has the Stage filter. -->
      <div data-filters class="flex shrink-0 items-center gap-2 overflow-x-auto border-b border-border-subtle px-4.5 py-2">
        {#if view === 'table'}{@render stageMenu()}{/if}
        <AuthenticityFilter {applications} filter={integrityFilter} onchange={setIntegrityFilter} />
      </div>
    {:else if view === 'board'}
      <div data-filters class="flex shrink-0 items-center gap-2 overflow-x-auto border-b border-border-subtle px-4 py-2">
        <AuthenticityFilter {applications} filter={integrityFilter} onchange={setIntegrityFilter} menu />
      </div>
    {/if}
    {#if view === 'table' && selected.length > 0}
      <SelectionBar
        count={selected.length}
        stages={moveTargets}
        onmove={moves.allowed ? moveSelected : undefined}
        disabled={moves.pending}
        onemail={canEmail && !isIntegrityFiltered(integrityFilter) ? () => (emailOpen = true) : undefined}
        onclear={() => (selectedIds = [])}
      />
    {/if}
  {/if}

  {#if error}
    <div class="p-4">
      <QueryError message={describeError(error, 'Could not load the candidates')} {onretry} inset />
    </div>
  {:else if pending}
    <div aria-hidden="true" class="flex flex-col gap-1 p-3">
      {#each [0, 1, 2, 3] as row (row)}<Skeleton class="h-12 w-full max-md:h-24" />{/each}
    </div>
  {:else if view === 'table'}
    <ApplicationTable
      applications={shown}
      label={`Candidates for ${jobTitle}`}
      fill={!sidebar.isMobile}
      sortId="job-candidates"
      {stages}
      {moves}
      extraColumns={[...integrityColumns, ratingColumn]}
      bind:selectedIds
      emptyText={filtering ? 'No candidates match these filters.' : 'No candidates yet. Applications appear here as they arrive.'}
      onclearfilters={filtering ? clearFilters : undefined}
    >
      {#snippet filters()}
        {@render stageMenu()}
        <AuthenticityFilter {applications} filter={integrityFilter} onchange={setIntegrityFilter} menu />
      {/snippet}
    </ApplicationTable>
  {:else if onBoard.length === 0}
    <Empty.Root class="gap-3 p-6">
      <Empty.Description>{filtering ? 'No candidates match these filters.' : 'No candidates yet. Applications appear here as they arrive.'}</Empty.Description>
      {#if filtering}<Button variant="outline" size="lg" onclick={clearFilters}>Clear filters</Button>{/if}
    </Empty.Root>
  {:else}
    <PipelineBoard applications={onBoard} {stages} {moves} />
  {/if}
  <!-- A move that worked is a quiet line; a failed one is a problem to fix (T1). -->
  <p role="status" class="px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4">{moves.failure ? '' : moves.announcement}</p>
  <FailureText message={moves.failure ? moves.announcement : ''} class="px-4.5 py-2 max-md:px-4" />
  <p role="status" class="px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4">{emailed}</p>
</Panel>

{#if canEmail}
  <EmailComposerSheet bind:open={emailOpen} recipients={selected} onsent={(message) => ((emailed = message), (selectedIds = []))} />
{/if}

{#snippet searchField(className: string)}
  <SearchField bind:value={search} label="Search candidates" class={className} />
{/snippet}

{#snippet stageMenu()}
  <FilterMenu label="Stage" options={stageOptions} total={applications.length} bind:value={stageFilter} />
{/snippet}
