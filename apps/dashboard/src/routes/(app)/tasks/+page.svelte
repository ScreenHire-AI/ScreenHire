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
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { sentenceStart } from '#lib/history.js';
  import { describeError } from '#lib/query.js';
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
  const shown = $derived(status === 'Open' ? everyone.filter((task) => inScope(task, scope)) : (done[scope].data?.data ?? []));
  const countOf = (which: TaskScope) =>
    status === 'Open' ? everyone.filter((task) => inScope(task, which)).length : done[which].data?.data.length;
  // The panel is Tasks whichever toggle is pressed, so its title does not repeat the toggle (W4); the table says which.
  const listLabel = $derived(status === 'Open' ? 'Open tasks' : 'Done tasks');

  // Who a task is for: a value of the Everyone's list only. Mine and For the team already say it (W7), so they have no
  // such column.
  const assignee = (task: TaskListItem) => (task.assigneeUserId ? sentenceStart(personName(task.assigneeUserId, team.data?.data, currentUserId)) : 'The team');
  const doneBy = (task: TaskListItem) => (task.completedByUserId ? sentenceStart(personName(task.completedByUserId, team.data?.data, currentUserId)) : '');

  // The table sorts (L2). Open tasks start with the one that has waited longest; done tasks with the newest finished.
  // Each has its own remembered sort, since they have different columns.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns = $derived<ColumnDef<typeof features, TaskListItem, any>[]>([
    { id: 'task', header: 'Task', accessorFn: (task) => task.note, sortFn: 'alphanumeric' },
    { id: 'candidate', header: 'Candidate', accessorFn: (task) => task.candidateName, sortFn: 'alphanumeric' },
    { id: 'job', header: 'Job', accessorFn: (task) => task.jobTitle, sortFn: 'alphanumeric' },
    { id: 'kind', header: 'Type', accessorFn: (task) => taskKindLabels[task.kind], sortFn: 'alphanumeric' },
    ...(scope === 'Everyone' ? [{ id: 'for', header: 'For', accessorFn: assignee, sortFn: 'alphanumeric' } satisfies ColumnDef<typeof features, TaskListItem, any>] : []),
    { id: 'created', header: 'Created', accessorFn: (task) => task.createdAt, sortFn: 'basic', sortDescFirst: true },
    ...(status === 'Completed'
      ? [
          { id: 'done', header: 'Done', accessorFn: (task) => task.completedAt ?? undefined, sortFn: 'basic', sortDescFirst: true, sortUndefined: 'last' },
          { id: 'doneBy', header: 'Done by', accessorFn: doneBy, sortFn: 'alphanumeric' },
        ] satisfies ColumnDef<typeof features, TaskListItem, any>[]
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
    const sorted = sorts[status].sorting[0]?.id;
    if (sorted && !columns.some((column) => column.id === sorted)) sorts[status].show(undefined);
  });
  const table = createTable({
    ...tableSortOptions(() => sorts[status]),
    features,
    get columns() {
      return columns;
    },
    get data() {
      return shown;
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
  <!-- The counts are on the Whose toggles, so the header does not repeat one (W4). -->
  <HeaderBand title="My tasks" />

  <div class="@container">
    <div class="flex w-full flex-col px-4 py-6 md:px-6">
      <Panel title="Tasks" flush>
        <!-- The filters, in a row under the header because the list has no search (section 7). Each group stays on one
             line (F5); in a narrow container the two groups stack, and the Whose toggles, whose counts all matter at a
             glance, stack one per line (section 11, Toggle). On a phone the Sort chip ends the row. -->
        <div class="@container/filters overflow-x-auto [scrollbar-width:thin] border-b border-border-subtle px-4.5 py-2 max-md:px-4">
          <div class="flex flex-col items-start gap-2 @md/filters:flex-row @md/filters:items-center @md/filters:gap-4">
            <div role="group" aria-label="Status" class="flex shrink-0 gap-1">
              <Toggle size="lg" bind:pressed={() => status === 'Open', () => (status = 'Open')}>Open</Toggle>
              <Toggle size="lg" bind:pressed={() => status === 'Completed', () => (status = 'Completed')}>Done</Toggle>
            </div>
            <div role="group" aria-label="Whose" class="flex shrink-0 flex-col items-start gap-1 @md/filters:flex-row">
              {#each scopes as option (option.value)}
                <Toggle size="lg" bind:pressed={() => scope === option.value, () => (scope = option.value)}>
                  {option.label}
                  {@const count = countOf(option.value)}
                  {#if tasks.isSuccess && count !== undefined}<Badge variant="count">{count}</Badge>{/if}
                </Toggle>
              {/each}
            </div>
            {#if sidebar.isMobile && shown.length > 0}<SortMenu {table} class="shrink-0" />{/if}
          </div>
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
        {:else if shown.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><ListTodoIcon /></Empty.Media>
              <Empty.Title>{status === 'Open' ? 'Nothing to do' : 'Nothing done yet'}</Empty.Title>
              <Empty.Description>
                {status === 'Open' ? 'Automations add tasks here when a workflow asks for a review or a to-do.' : 'Tasks you and your team complete show here.'}
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
                    {#each group.headers as header (header.id)}<SortHeader {header} class={['px-3 first:pl-4.5', header.id === 'created' && 'whitespace-nowrap']} />{/each}
                  </Table.Row>
                {/each}
              </Table.Header>
              <Table.Body>
                {#each sorted as task (task.id)}
                  <!-- The whole row is the link to where the work is done (design system section 9). -->
                  <Table.Row class="group relative h-12 hover:bg-muted">
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
                        <Table.Cell class="max-w-48"><span class="flex min-w-0"><TruncatedText text={assignee(task)} /></span></Table.Cell>
                      {:else if header.id === 'created'}
                        <Table.Cell class="whitespace-nowrap text-muted-foreground"><span class="relative"><DateText at={task.createdAt} /></span></Table.Cell>
                      {:else if header.id === 'done'}
                        <Table.Cell class="whitespace-nowrap text-muted-foreground"><span class="relative">{#if task.completedAt}<DateText at={task.completedAt} />{/if}</span></Table.Cell>
                      {:else if header.id === 'doneBy'}
                        <Table.Cell class="max-w-48"><span class="flex min-w-0"><TruncatedText text={doneBy(task)} /></span></Table.Cell>
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
  </div>
</main>
