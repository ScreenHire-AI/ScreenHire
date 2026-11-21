<script lang="ts">
  import ListTodoIcon from '~icons/lucide/list-todo';
  import type { TaskListItem, TaskScope, TaskStatusFilter } from '@screenhire/api-client';
  import { createListTasks, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import { page } from '$app/state';
  import DateText from '#lib/components/date-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RecordTabs from '#lib/components/record-tabs.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { sentenceStart } from '#lib/history.js';
  import { describeError } from '#lib/query.js';
  import { matchesWords } from '#lib/settings.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { taskHref } from '#lib/tasks.svelte.js';
  import { assigneeText, personName, taskKindLabels } from '#lib/team.js';

  // The tasks automations make (AssignReviewer, CreateTask, QueueForRejectionReview), on the list template: a table, one
  // row per task, with a list on a phone. Mine is the tasks assigned to the person, For the team the tasks assigned to
  // nobody in particular, and Everyone's every task the person can see. One list of everyone's open tasks gives all three
  // and their counts, split the way the API's TaskScope splits them. Done tasks are each scope's newest 200
  // (TaskEndpoints), so each scope asks for its own: split from everyone's, a busy team's newest 200 would hide the
  // person's older ones. New tasks arrive live with AutomationRunFinished, and completions with ApplicationTaskCompleted
  // and ApplicationTaskReopened (#lib/realtime.ts). A task is ticked off where its work is done, on the candidate's Next
  // steps, so each row links there.
  const scopes: readonly { value: TaskScope; label: string }[] = [
    { value: 'Mine', label: 'Mine' },
    { value: 'Team', label: 'For the team' },
    { value: 'Everyone', label: "Everyone's" },
  ];
  let status = $state<TaskStatusFilter>('Open');
  let scope = $state<TaskScope>('Mine');
  const open = createListTasks(() => ({ status: 'Open', scope: 'Everyone' }), () => ({ query: { enabled: status === 'Open' } }));
  const done = Object.fromEntries(
    scopes.map(({ value }) => [
      value,
      createListTasks(() => ({ status: 'Completed', scope: value }), () => ({ query: { enabled: status === 'Completed' } })),
    ]),
  ) as Record<TaskScope, typeof open>;
  const tasks = $derived(status === 'Open' ? open : done[scope]);
  const team = createListTeamMembers();
  const currentUserId = $derived(page.data.user?.id);
  const everyone = $derived((open.data?.data ?? []).toSorted((a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id)));
  const inScope = (task: TaskListItem, which: TaskScope) =>
    which === 'Everyone' || (which === 'Mine' ? task.assigneeUserId === currentUserId && currentUserId !== undefined : task.assigneeUserId === null);
  const listed = $derived(status === 'Open' ? everyone.filter((task) => inScope(task, scope)) : (done[scope].data?.data ?? []));
  // Search reads what a row shows: the task, the candidate, the job and the type, every word somewhere in them.
  let search = $state('');
  const shown = $derived(
    search.trim() === '' ? listed : listed.filter((task) => matchesWords(`${task.note} ${task.candidateName} ${task.jobTitle} ${taskKindLabels[task.kind]}`, search)),
  );
  const countOf = (which: TaskScope) =>
    status === 'Open' ? everyone.filter((task) => inScope(task, which)).length : done[which].data?.data.length;
  // The list page (design system section 11): whose tasks are the tabs under the title, with their counts, and the
  // panel is named after what it shows, Open or Done, the pills under its header.
  // Nothing to list in this state: no search over nothing (design system section 7). Open and Done stay, because Done is
  // only asked for when it is chosen, so an empty Open list cannot say whether Done has anything.
  const bare = $derived(tasks.isSuccess && listed.length === 0);
  const listLabel = $derived(status === 'Open' ? 'Open tasks' : 'Done tasks');
  const scopeTabs = $derived(scopes.map((option) => ({ value: option.value, label: option.label, count: tasks.isSuccess ? countOf(option.value) : undefined })));

  // Who a task is for: a value of the Everyone's list only. Mine and For the team already say it (W7), so they have no
  // such column.
  const assignee = (task: TaskListItem) => (task.assigneeUserId ? sentenceStart(personName(task.assigneeUserId, team.data?.data, currentUserId)) : 'The team');
  const doneBy = (task: TaskListItem) => (task.completedByUserId ? sentenceStart(personName(task.completedByUserId, team.data?.data, currentUserId)) : '');
  // The names sit on the rows because the table caches each row's values until its data changes: a name read in an
  // accessor would stay the one from before the team loaded, and a remembered sort on it would order the rows wrongly.
  type TaskTableRow = TaskListItem & { assignee: string; doneBy: string };
  const rows = $derived(shown.map((task): TaskTableRow => ({ ...task, assignee: assignee(task), doneBy: doneBy(task) })));

  // The table sorts (L2). Open tasks start with the one that has waited longest; done tasks with the newest finished.
  // Each has its own remembered sort, since they have different columns.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns = $derived<ColumnDef<typeof features, TaskTableRow, any>[]>([
    { id: 'task', header: 'Task', accessorFn: (task) => task.note, sortFn: 'alphanumeric' },
    { id: 'candidate', header: 'Candidate', accessorFn: (task) => task.candidateName, sortFn: 'alphanumeric' },
    { id: 'job', header: 'Job', accessorFn: (task) => task.jobTitle, sortFn: 'alphanumeric' },
    { id: 'kind', header: 'Type', accessorFn: (task) => taskKindLabels[task.kind], sortFn: 'alphanumeric' },
    ...(scope === 'Everyone' ? [{ id: 'for', header: 'For', accessorFn: (task) => task.assignee, sortFn: 'alphanumeric' } satisfies ColumnDef<typeof features, TaskTableRow, any>] : []),
    { id: 'created', header: 'Created', accessorFn: (task) => task.createdAt, sortFn: 'basic', sortDescFirst: true },
    ...(status === 'Completed'
      ? [
          { id: 'done', header: 'Done', accessorFn: (task) => task.completedAt ?? undefined, sortFn: 'basic', sortDescFirst: true, sortUndefined: 'last' },
          { id: 'doneBy', header: 'Done by', accessorFn: (task) => task.doneBy, sortFn: 'alphanumeric' },
        ] satisfies ColumnDef<typeof features, TaskTableRow, any>[]
      : []),
  ]);
  const columnIds = ['task', 'candidate', 'job', 'kind', 'for', 'created', 'done', 'doneBy'];
  const sorts = {
    Open: createTableSort('tasks-open', { id: 'created', desc: false }, columnIds),
    Completed: createTableSort('tasks-done', { id: 'done', desc: true }, columnIds),
  };
  // A remembered sort can name a column this scope does not have (For, outside Everyone's): the table then opens on its
  // default instead of sorting on nothing.
  $effect(() => {
    const sortedColumn = sorts[status].sorting[0]?.id;
    if (sortedColumn && !columns.some((column) => column.id === sortedColumn)) sorts[status].show(undefined);
  });
  const table = createTable({
    ...tableSortOptions(() => sorts[status]),
    features,
    get columns() {
      return columns;
    },
    get data() {
      return rows;
    },
    getRowId: (task) => task.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));

  const sidebar = Sidebar.useSidebar();
  const rowLink = 'font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline';
</script>

<svelte:head>
  <title>My tasks · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'My tasks' }]} />

<main class="flex min-w-0 flex-col">
  <Tabs.Root value={scope} onValueChange={(value) => (scope = value as TaskScope)}>
  <HeaderBand title="My tasks">
    {#snippet tabs()}<RecordTabs tabs={scopeTabs} label="Whose tasks" />{/snippet}
  </HeaderBand>

  <Tabs.Content value={scope} class="@container">
    <div class="flex w-full flex-col px-4 py-6 md:px-6">
      <Panel title={listLabel} flush>
        {#snippet tools()}
          {#if !bare}<SearchField bind:value={search} label="Search tasks" placeholder="Search" class="ml-auto flex-[0_1_15rem] max-md:ml-0 max-md:flex-1" />{/if}
        {/snippet}
        <!-- Open or Done in one row under the header, as every list page's state filters are (section 11). Done is each
             scope's newest 200, so it carries no count that would read as a total. On a phone the Sort chip ends the row. -->
        <div class="flex items-center gap-2 overflow-x-auto [scrollbar-width:thin] border-b border-border-subtle px-4.5 py-2 max-md:px-4">
          <div role="group" aria-label="Status" class="flex shrink-0 gap-1">
            <Toggle size="lg" bind:pressed={() => status === 'Open', () => (status = 'Open')}>Open</Toggle>
            <Toggle size="lg" bind:pressed={() => status === 'Completed', () => (status = 'Completed')}>Done</Toggle>
          </div>
          {#if sidebar.isMobile && shown.length > 0}<SortMenu {table} class="ml-auto shrink-0" />{/if}
        </div>

        {#if tasks.isError}
          <div class="p-4">
            <QueryError message={describeError(tasks.error, 'Could not load tasks')} onretry={() => tasks.refetch()} inset />
          </div>
        {:else if tasks.isPending}
          <!-- Rows of the table's shape (E1). -->
          <div aria-hidden="true" class="flex flex-col gap-1 p-3">
            {#each [0, 1, 2] as row (row)}<Skeleton class="h-12 w-full max-md:h-24" />{/each}
          </div>
          <p class="sr-only" role="status">Loading tasks…</p>
        {:else if shown.length === 0 && listed.length > 0}
          <p role="status" class="p-4.5 text-muted-foreground max-md:p-4">No task matches "{search.trim()}".</p>
        {:else if shown.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><ListTodoIcon /></Empty.Media>
              <Empty.Title>{status === 'Open' ? 'Nothing to do' : 'Nothing done yet'}</Empty.Title>
              <Empty.Description>
                {status === 'Open' ? 'Automations add tasks here when a workflow asks for a scorecard or a to-do.' : 'Tasks you and your team complete show here.'}
              </Empty.Description>
            </Empty.Header>
            {#if scope !== 'Everyone' && (countOf('Everyone') ?? 0) > 0}
              <Empty.Content><Button variant="outline" size="lg" onclick={() => (scope = 'Everyone')}>Show everyone's</Button></Empty.Content>
            {/if}
          </Empty.Root>
        {:else if sidebar.isMobile}
          <ul aria-label={listLabel}>
            {#each sorted as task (task.id)}
              <ListItem href={taskHref(task)} title={task.note}>
                {#snippet meta()}<DateText at={status === 'Completed' && task.completedAt ? task.completedAt : task.createdAt} />{/snippet}
                {#snippet facts()}
                  <Badge variant="muted" class="max-w-full"><TruncatedText text={task.candidateName} /></Badge>
                  <Badge variant="muted" class="max-w-full"><TruncatedText text={task.jobTitle} /></Badge>
                  <Badge variant="muted">{taskKindLabels[task.kind]}</Badge>
                  {#if scope === 'Everyone'}<Badge variant="muted">{assigneeText(task.assigneeUserId, team.data?.data, currentUserId)}</Badge>{/if}
                {/snippet}
              </ListItem>
            {/each}
          </ul>
        {:else}
          <div class="overflow-x-auto">
            <Table.Root aria-label={listLabel}>
              <Table.Header>
                {#each table.getHeaderGroups() as group (group.id)}
                  <Table.Row class="bg-muted hover:bg-muted">
                    {#each group.headers as header (header.id)}<SortHeader {header} class={['first:pl-4.5', header.id === 'created' && 'whitespace-nowrap']} />{/each}
                  </Table.Row>
                {/each}
              </Table.Header>
              <Table.Body>
                {#each sorted as task (task.id)}
                  <!-- The whole row is the link to where the work is done (design system section 9). -->
                  <Table.Row class="group relative h-12 hover:bg-muted">
                    <!-- One branch per entry in `columns`, which draws the headers. -->
                    {#each table.getHeaderGroups()[0]!.headers as header (header.id)}
                      {#if header.id === 'task'}
                        <Table.Cell class="max-w-96 pl-4.5">
                          <span class="flex min-w-0"><TruncatedText text={task.note} href={taskHref(task)} class={rowLink} /></span>
                        </Table.Cell>
                      {:else if header.id === 'candidate'}
                        <Table.Cell class="max-w-56"><span class="flex min-w-0"><TruncatedText text={task.candidateName} /></span></Table.Cell>
                      {:else if header.id === 'job'}
                        <Table.Cell class="max-w-56"><span class="flex min-w-0"><TruncatedText text={task.jobTitle} /></span></Table.Cell>
                      {:else if header.id === 'kind'}
                        <Table.Cell class="whitespace-nowrap">{taskKindLabels[task.kind]}</Table.Cell>
                      {:else if header.id === 'for'}
                        <Table.Cell class="max-w-48"><span class="flex min-w-0"><TruncatedText text={task.assignee} /></span></Table.Cell>
                      {:else if header.id === 'created'}
                        <Table.Cell class="whitespace-nowrap text-muted-foreground"><span class="relative"><DateText at={task.createdAt} /></span></Table.Cell>
                      {:else if header.id === 'done'}
                        <Table.Cell class="whitespace-nowrap text-muted-foreground"><span class="relative">{#if task.completedAt}<DateText at={task.completedAt} />{/if}</span></Table.Cell>
                      {:else if header.id === 'doneBy'}
                        <Table.Cell class="max-w-48"><span class="flex min-w-0"><TruncatedText text={task.doneBy} /></span></Table.Cell>
                      {/if}
                    {/each}
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    </div>
  </Tabs.Content>
  </Tabs.Root>
</main>
