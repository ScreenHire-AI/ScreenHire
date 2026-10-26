<script lang="ts" module>
  import type { LastUpdate, TemplateUse } from '@screenhire/api-client';

  /** One template as the Templates page lists it, whatever its kind. */
  export interface TemplateRow {
    id: string;
    name: string;
    builtIn: boolean;
    archived: boolean;
    href: string;
    usage: TemplateUse | undefined;
    lastUpdated: LastUpdate;
    /** Hiring: what it is for. */
    description?: string | null;
    /** Hiring: the newest published version and the draft's, if any. */
    published?: number | null;
    draft?: number | null;
    /** Scorecards: how many questions. */
    questions?: number;
    /** Emails: the subject, and its pieces with each variable as a word. */
    subject?: string;
    subjectParts?: readonly { text: string; variable: boolean }[];
  }
</script>

<script lang="ts">
  import FileStackIcon from '@lucide/svelte/icons/file-stack';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import type { TeamMember } from '@screenhire/api-client';
  import {
    createColumnHelper,
    createSortedRowModel,
    createTable,
    FlexRender,
    renderSnippet,
    rowSortingFeature,
    sortFn_alphanumeric,
    sortFn_basic,
    tableFeatures,
    type ColumnDef,
  } from '@tanstack/svelte-table';
  import type { Snippet } from 'svelte';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TemplateSubject from '#lib/components/template-subject.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { actorName } from '#lib/history.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { filterCounts, matchesFilter, templateFilters, templateKindViews, usedByLabel, type TemplateFilter, type TemplateKind } from '#lib/templates.js';

  // One tab of the Templates page (design system section 21): the list template's table panel (section 7). Its header
  // holds the tab's one sentence and the search; under it the filters with their counts (T4), then a table that sorts
  // every column (L2) and opens on Last updated, newest first, with the person's sort remembered per kind. The name is
  // semibold with Built in after it for ScreenHire's own (W6, L1); a draft is the Draft badge in Version; Used by is a
  // count; Last updated says who. Phones get list items with the Sort chip (section 8). Archived templates show only
  // under Archived. The row menu is the page's: `menu` renders a row's items, and `hasMenu` says whether it has any.
  let {
    kind,
    rows,
    pending,
    error,
    onretry,
    usagePending,
    members,
    currentUserId,
    menu,
    hasMenu,
    message = '',
    failure = '',
    oncreate,
  }: {
    kind: TemplateKind;
    rows: readonly TemplateRow[];
    pending: boolean;
    /** Why the list could not load, for `QueryError`. */
    error?: string;
    onretry: () => unknown;
    /** The usage is still loading: its cells and the filters that need it wait. */
    usagePending: boolean;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    menu: Snippet<[TemplateRow]>;
    hasMenu: (row: TemplateRow) => boolean;
    /** What the last row action did, in the panel's footer. */
    message?: string;
    failure?: string;
    /** Starts a new template of this kind, for a role that may; an empty list offers it (E1). */
    oncreate?: () => void;
  } = $props();

  const sidebar = Sidebar.useSidebar();
  const view = $derived(templateKindViews[kind]);

  let filter = $state<TemplateFilter>('all');
  let search = $state('');
  const counts = $derived(filterCounts(rows));
  const shown = $derived.by(() => {
    const words = search.trim().toLocaleLowerCase();
    return rows.filter(
      (row) =>
        matchesFilter(row, filter) && (!words || [row.name, row.description ?? '', row.subject ?? ''].some((text) => text.toLocaleLowerCase().includes(words))),
    );
  });
  const filtering = $derived(filter !== 'all' || Boolean(search.trim()));

  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  const column = createColumnHelper<typeof features, TemplateRow>();
  // `any`: one list mixes columns of different value types.
  type Column = ColumnDef<typeof features, TemplateRow, any>;
  // svelte-ignore state_referenced_locally
  const kindOf = kind;
  const middle: Record<TemplateKind, Column[]> = {
    hiring: [column.accessor((row) => row.description ?? '', { id: 'description', header: 'What it is for', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(textCell, row.original.description ?? '') })],
    scorecards: [column.accessor((row) => row.questions ?? 0, { id: 'questions', header: 'Questions', sortFn: 'basic', cell: ({ row }) => String(row.original.questions ?? 0) })],
    emails: [column.accessor((row) => row.subject ?? '', { id: 'subject', header: 'Subject', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(subjectCell, row.original) })],
  };
  const columns: Column[] = [
    column.accessor('name', { header: 'Name', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(nameCell, row.original) }),
    ...middle[kindOf],
    // Used by sorts on its count: while usage loads every row is undefined, which sorts last.
    column.accessor((row) => row.usage?.usedBy.length, { id: 'usedBy', header: 'Used by', sortFn: 'basic', sortUndefined: 'last', cell: ({ row }) => renderSnippet(usedByCell, row.original) }),
    column.accessor((row) => row.usage?.lastUsedAt ?? undefined, {
      id: 'lastUsed',
      header: kindOf === 'emails' ? 'Last sent' : 'Last used',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(lastUsedCell, row.original),
    }),
    ...(kindOf === 'hiring'
      ? [column.accessor((row) => row.published ?? row.draft ?? 0, { id: 'version', header: 'Version', sortFn: 'basic', cell: ({ row }) => renderSnippet(versionCell, row.original) })]
      : []),
    column.accessor((row) => row.lastUpdated.at ?? undefined, {
      id: 'updatedAt',
      header: 'Last updated',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(updatedCell, row.original),
    }),
    column.display({ id: 'actions', header: () => renderSnippet(actionsHeader, undefined), cell: ({ row }) => renderSnippet(actionsCell, row.original) }),
  ];
  const sort = createTableSort(
    `templates-${kindOf}`,
    { id: 'updatedAt', desc: true },
    columns.map((definition) => definition.id ?? 'name').filter((id) => id !== 'actions'),
  );
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return shown;
    },
    getRowId: (row) => row.id,
  });
  const tableRows = $derived(table.getRowModel().rows);

  // A table wider than its panel (a tablet) scrolls sideways inside it, and the name column stays put (design system
  // section 8). A stuck cell holds the name's row-covering link, so while the table scrolls the link covers the name
  // cell only, as on the candidate table; while it fits, the whole row is the link (section 9).
  let scrollerWidth = $state(0);
  let tableWidth = $state(0);
  let scrolledSideways = $state(false);
  const overflowing = $derived(tableWidth > scrollerWidth + 1);
  // While the table is scrolled sideways, the name column has an edge, so cells sliding under it do not show beside it.
  const nameEdge = 'after:absolute after:inset-y-0 after:right-0 after:w-px after:bg-border';

  const who = (row: TemplateRow) => actorName(row.lastUpdated, members, currentUserId);

  function clearFilters() {
    filter = 'all';
    search = '';
  }
</script>

<Panel label={view.noun} flush footer={message || failure ? footer : undefined}>
  {#snippet tools()}
    <p class="min-w-0 flex-1 text-muted-foreground max-md:hidden"><TruncatedText text={view.sentence} /></p>
    <SearchField bind:value={search} label={`Search ${view.noun.toLocaleLowerCase()}`} class="ml-auto flex-[0_1_15rem] max-md:ml-0 max-md:flex-1" />
  {/snippet}

  <!-- On a phone the sentence moves under the header, where it has the width to read whole. -->
  <p class="border-b border-border-subtle px-4 py-2 text-muted-foreground md:hidden">{view.sentence}</p>

  <!-- The filters: one line that scrolls inside the panel rather than wrapping (F5), each with its count (T4). -->
  <div class="flex items-center gap-2 border-b border-border-subtle px-4.5 py-2 max-md:px-4">
    <div class="min-w-0 flex-1 overflow-x-auto">
      <div role="group" aria-label="Show" class="flex w-max gap-1">
        {#each templateFilters as option (option.value)}
          <Toggle size="lg" bind:pressed={() => filter === option.value, () => (filter = option.value)}>
            {option.label}
            {#if !pending && (!usagePending || option.value === 'all' || option.value === 'archived')}<Badge variant="count">{counts[option.value]}</Badge>{/if}
          </Toggle>
        {/each}
      </div>
    </div>
    {#if sidebar.isMobile && !pending}<SortMenu {table} class="shrink-0" />{/if}
  </div>

  {#if error}
    <div class="p-4"><QueryError message={error} {onretry} inset /></div>
  {:else if pending}
    <!-- Rows of the table's shape: a name, a few facts and a date (E1). -->
    <div aria-hidden="true" class="flex flex-col">
      {#each [0, 1, 2] as row (row)}
        <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
          <Skeleton class="h-4 w-40" />
          <Skeleton class="h-4 min-w-0 flex-1" />
          <Skeleton class="h-4 w-24" />
        </div>
      {/each}
    </div>
    <p class="sr-only" role="status">Loading templates…</p>
  {:else if shown.length === 0}
    <Empty.Root>
      <Empty.Header>
        <Empty.Media variant="icon"><FileStackIcon /></Empty.Media>
        <Empty.Title>{filter === 'archived' && !search.trim() ? 'Nothing archived' : filtering ? 'No templates match' : 'No templates yet'}</Empty.Title>
        {#if filter === 'archived' && !search.trim()}
          <Empty.Description>An archived template leaves the pickers until someone restores it.</Empty.Description>
        {/if}
      </Empty.Header>
      {#if filtering}
        <Empty.Content><Button variant="outline" size="lg" onclick={clearFilters}>Show all</Button></Empty.Content>
      {:else if oncreate}
        <Empty.Content><Button variant="outline" size="lg" onclick={oncreate}><PlusIcon aria-hidden="true" />New template</Button></Empty.Content>
      {/if}
    </Empty.Root>
  {:else if sidebar.isMobile}
    <ul aria-label={view.noun}>
      {#each tableRows as tableRow (tableRow.id)}
        {@const row = tableRow.original}
        <ListItem href={row.href} title={row.name}>
          {#snippet meta()}{#if row.lastUpdated.at}<DateText at={row.lastUpdated.at} />{/if}{/snippet}
          {#snippet facts()}
            {#if row.builtIn}<BuiltInBadge />{/if}
            {#if row.usage?.usedBy.length}<Badge variant="muted">Used by {usedByLabel(kind, row.usage.usedBy.length).toLocaleLowerCase()}</Badge>{/if}
            {#if row.draft}<StatusBadge status="draft" label={`Draft ${row.draft}`} quiet />{/if}
          {/snippet}
        </ListItem>
      {/each}
    </ul>
  {:else}
    <!-- relative: the Actions header's sr-only label is absolute, and would escape the scroller and widen the page (F1). -->
    <div class="relative overflow-x-auto" bind:clientWidth={scrollerWidth} onscroll={(event) => (scrolledSideways = event.currentTarget.scrollLeft > 0)}>
      <table aria-label={view.noun} bind:offsetWidth={tableWidth} class="w-full border-collapse text-left">
        <thead class="bg-muted">
          {#each table.getHeaderGroups() as group (group.id)}
            <tr>
              {#each group.headers as header (header.id)}
                <SortHeader
                  {header}
                  class={[
                    'px-3',
                    header.id === 'name' && 'px-4.5',
                    header.id === 'name' && overflowing && 'sticky left-0 z-[1] bg-muted',
                    header.id === 'name' && overflowing && scrolledSideways && nameEdge,
                    header.id === 'actions' && 'w-14 px-4 text-right',
                    (header.id === 'updatedAt' || header.id === 'lastUsed') && 'text-right',
                  ]}
                />
              {/each}
            </tr>
          {/each}
        </thead>
        <tbody>
          {#each tableRows as tableRow (tableRow.id)}
            <!-- The whole row is the link (section 9): the name's link covers it, and cells with controls sit above it. -->
            <tr class="group relative h-12 border-b border-border-subtle last:border-b-0 hover:bg-muted">
              {#each tableRow.getAllCells() as cell (cell.id)}
                <td
                  class={[
                    'px-3 whitespace-nowrap',
                    cell.column.id === 'name' && 'max-w-72 px-4.5',
                    // A stuck cell covers what scrolls under it, so it carries the row's hover tint itself.
                    cell.column.id === 'name' && overflowing && 'sticky left-0 z-[1] bg-card group-hover:bg-muted',
                    cell.column.id === 'name' && overflowing && scrolledSideways && nameEdge,
                    // The description or the subject takes the room the other columns leave, and is cut (F5, L1).
                    (cell.column.id === 'description' || cell.column.id === 'subject') && 'w-full max-w-0 min-w-40',
                    cell.column.id !== 'name' && 'text-muted-foreground',
                    (cell.column.id === 'updatedAt' || cell.column.id === 'lastUsed') && 'text-right tabular-nums',
                    (cell.column.id === 'questions' || cell.column.id === 'version') && 'tabular-nums',
                    cell.column.id === 'actions' && 'px-4 text-right',
                  ]}
                >
                  <FlexRender {cell} />
                </td>
              {/each}
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</Panel>

{#snippet footer()}
  <div class="flex flex-wrap items-center gap-2">
    <p role="status" class="text-muted-foreground empty:hidden">{message}</p>
    <FailureText message={failure} />
  </div>
{/snippet}

{#snippet nameCell(row: TemplateRow)}
  <!-- The row's name, with at most one badge after it (L1). -->
  <span class="flex min-w-0 items-center gap-2">
    <TruncatedText text={row.name} href={row.href} class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline" />
    {#if row.builtIn}<BuiltInBadge class="shrink-0" />{/if}
  </span>
{/snippet}

{#snippet textCell(text: string)}{#if text}<span class="relative block min-w-0"><TruncatedText {text} /></span>{/if}{/snippet}

{#snippet subjectCell(row: TemplateRow)}
  <span class="relative block min-w-0"><TemplateSubject parts={row.subjectParts ?? []} /></span>
{/snippet}

{#snippet usedByCell(row: TemplateRow)}
  {#if row.usage}
    <span class={row.usage.usedBy.length > 0 ? 'text-foreground' : ''}>{usedByLabel(kind, row.usage.usedBy.length)}</span>
  {:else if usagePending}
    <Skeleton class="h-4 w-16" />
  {/if}
{/snippet}

{#snippet lastUsedCell(row: TemplateRow)}
  {#if row.usage?.lastUsedAt}
    <span class="relative"><DateText at={row.usage.lastUsedAt} /></span>
  {:else if row.usage}
    Never
  {:else if usagePending}
    <Skeleton class="ml-auto h-4 w-20" />
  {/if}
{/snippet}

{#snippet versionCell(row: TemplateRow)}
  <!-- The newest published version, then a draft as its status badge (W6). -->
  <span class="flex items-center gap-2">
    {#if row.published}<span class="text-foreground">{row.published}</span>{/if}
    {#if row.draft}<StatusBadge status="draft" label={`Draft ${row.draft}`} quiet />{/if}
  </span>
{/snippet}

{#snippet updatedCell(row: TemplateRow)}
  {@const name = who(row)}
  <span class="relative"
    >{#if row.lastUpdated.at}<DateText at={row.lastUpdated.at} />{#if name}{' '}by {name}{/if}{:else if name}By {name}{/if}</span
  >
{/snippet}

{#snippet actionsHeader()}<span class="sr-only">Actions</span>{/snippet}

{#snippet actionsCell(row: TemplateRow)}
  {#if hasMenu(row)}<span class="relative inline-flex">{@render menu(row)}</span>{/if}
{/snippet}
