<script lang="ts">
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import { canChangeHiringData } from '#lib/auth.js';
  import { matchesSearch } from '#lib/candidate-table.js';
  import ApplicationTable from '#lib/components/application-table.svelte';
  import EmailSelectionButton from '#lib/components/email-selection-button.svelte';
  import EmailSheet from '#lib/components/email-sheet.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import IntegrityFilterMenu from '#lib/components/integrity-filter.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PipelineBoard from '#lib/components/pipeline-board.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Kbd } from '#lib/components/ui/kbd/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { integrityColumns } from '#lib/integrity-columns.js';
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
  import { cn } from '#lib/utils.js';

  // A job's candidates as a table or a board, in the job page's main column (design system section 7). Search, the
  // integrity filter and a bulk move sit in the panel header, the stage filter on a row under it. The page filters
  // nothing itself, and shows the selected candidate (`activeId`) in its side column.
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
    activeId = $bindable(null),
  }: {
    jobTitle: string;
    stages: readonly WorkflowStage[];
    applications: readonly ApplicationSummary[];
    pending: boolean;
    /** The applications query's error, or null. */
    error: unknown;
    onretry: () => unknown;
    moves: Moves;
    /** Shared with the job's integrity overview, whose legend sets it through `setIntegrityFilter`. */
    integrityFilter?: IntegrityFilter;
    view?: 'table' | 'board';
    /** The keyboard cursor's row (J / K), shown by the page in its side column. */
    activeId?: string | null;
  } = $props();

  // The sidebar's own phone test (under md), where the search moves out of the header.
  const sidebar = Sidebar.useSidebar();

  let stageFilter = $state<string | null>(null);
  let search = $state('');
  let selectedIds = $state<string[]>([]);

  const searched = $derived(applications.filter((application) => matchesSearch(application, search)));
  const shown = $derived(
    searched.filter((application) => (!stageFilter || application.stage.id === stageFilter) && matchesIntegrityFilter(application, integrityFilter)),
  );
  const filtering = $derived(Boolean(stageFilter) || Boolean(search.trim()) || isIntegrityFiltered(integrityFilter));
  const moveTargets = $derived(bulkMoveStages(stages, integrityFilter));
  // Only the rows on screen: a ticked row that a search or filter hides is never moved with the rest.
  const selected = $derived(shown.filter((application) => selectedIds.includes(application.id)));
  // Email to the selection, hidden under an integrity filter for the reason bulkMoveStages gives (ADR-0004).
  const emailTemplates = createListEmailTemplates();
  const canEmail = $derived(Boolean(emailTemplates.data?.data.sendingEnabled) && canChangeHiringData(page.data.user));
  let emailOpen = $state(false);
  let emailed = $state('');
  const countIn = (stageId: string) => applications.filter((application) => application.stage.id === stageId).length;
  const views = [
    { id: 'table', label: 'Table' },
    { id: 'board', label: 'Board' },
  ] as const;

  /**
   * A new integrity filter starts a new selection, so rows picked under one filter are never moved under another
   * filter's menu (bulkMoveStages, ADR-0004). The filter applies to the table, so it shows the table.
   */
  export function setIntegrityFilter(next: IntegrityFilter) {
    integrityFilter = next;
    selectedIds = [];
    view = 'table';
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
    // The ones that failed stay selected, so they can be moved again.
    selectedIds = await moves.moveAll(selected, stage);
  }
</script>

<!-- V is a shortcut for the focusable rows and cards inside, whose key presses bubble to the panel. On a phone the
     header has no room for a usable search beside the view switch, so the search takes its own row under it (F5). -->
<Panel title="Pipeline" count={pending ? undefined : applications.length} flush {onkeydown}>
  {#snippet tools()}
    {#if !sidebar.isMobile}{@render searchField('flex-[0_1_15rem] @max-3xl/panel:flex-1')}{/if}
    {#if view === 'table'}
      <IntegrityFilterMenu bind:filter={() => integrityFilter, setIntegrityFilter} />
      {#if selected.length > 0}
        <DropdownMenu.Root>
          <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline' }), 'shrink-0')}>Move {selected.length} selected…</DropdownMenu.Trigger>
          <DropdownMenu.Content align="end" class="w-52">
            {#each moveTargets as stage (stage.id)}
              <DropdownMenu.Item onSelect={() => moveSelected(stage)}>{stage.name}</DropdownMenu.Item>
            {/each}
          </DropdownMenu.Content>
        </DropdownMenu.Root>
        {#if canEmail && !isIntegrityFiltered(integrityFilter)}
          <EmailSelectionButton count={selected.length} onclick={() => (emailOpen = true)} />
        {/if}
      {/if}
    {/if}
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
  {#if view === 'table'}
    <!-- The stage filter: one line that scrolls inside the panel rather than wrapping (F5). -->
    <div class="flex items-center gap-2 overflow-x-auto border-b border-border-subtle px-4.5 py-2 max-md:px-4">
      <div role="group" aria-label="Stage" class="flex shrink-0 gap-1">
        {#each [{ id: null, name: 'All', count: null }, ...stages.map((stage) => ({ id: stage.id, name: stage.name, count: countIn(stage.id) }))] as option (option.id ?? 'all')}
          <Toggle size="lg" bind:pressed={() => stageFilter === option.id, () => (stageFilter = option.id)}>
            {option.name}
            {#if option.count !== null}<Badge variant="count">{option.count}</Badge>{/if}
          </Toggle>
        {/each}
      </div>
    </div>
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
      {stages}
      {moves}
      extraColumns={integrityColumns}
      bind:activeId
      bind:selectedIds
      emptyText={filtering ? 'No candidates match these filters.' : 'No candidates yet. Applications appear here as they arrive.'}
      onclearfilters={filtering ? clearFilters : undefined}
    />
  {:else if searched.length === 0}
    <Empty.Root class="gap-3 p-6">
      <Empty.Description>{search.trim() ? 'No candidates match this search.' : 'No candidates yet. Applications appear here as they arrive.'}</Empty.Description>
      {#if search.trim()}<Button variant="outline" size="lg" onclick={clearFilters}>Clear filters</Button>{/if}
    </Empty.Root>
  {:else}
    <PipelineBoard applications={searched} {stages} {moves} />
  {/if}
  <!-- A move that worked is a quiet line; a failed one is a problem to fix (T1). -->
  <p role="status" class="px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4">{moves.failure ? '' : moves.announcement}</p>
  <FailureText message={moves.failure ? moves.announcement : ''} class="px-4.5 py-2 max-md:px-4" />
  <p role="status" class="px-4.5 py-2 text-body text-muted-foreground empty:hidden max-md:px-4">{emailed}</p>
</Panel>

{#if canEmail}
  <EmailSheet bind:open={emailOpen} recipients={selected} onsent={(message) => ((emailed = message), (selectedIds = []))} />
{/if}

{#snippet searchField(className: string)}
  <SearchField bind:value={search} label="Search candidates" class={className} />
{/snippet}
