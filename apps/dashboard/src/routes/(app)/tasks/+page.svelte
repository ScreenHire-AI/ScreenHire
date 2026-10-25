<script lang="ts">
  import ListTodoIcon from '@lucide/svelte/icons/list-todo';
  import type { TaskListItem, TaskStatusFilter } from '@screenhire/api-client';
  import { createListTasks, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import { can } from '#lib/permissions.js';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import TaskList from '#lib/components/task-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { describeError } from '#lib/query.js';
  import { createTaskCompletion } from '#lib/tasks.svelte.js';

  // The tasks automations make (AssignReviewer, CreateTask, QueueForRejectionReview), on the list template. Mine is the
  // tasks assigned to the person, For the team the tasks assigned to nobody in particular, and Everyone's every task the
  // person can see. One list of everyone's tasks gives all three and their counts, split the way the API's TaskScope
  // splits them. New tasks arrive live with AutomationRunFinished, and completions with ApplicationTaskCompleted and
  // ApplicationTaskReopened (#lib/realtime.ts).
  type Scope = 'Mine' | 'Team' | 'Everyone';
  const scopes: readonly { value: Scope; label: string }[] = [
    { value: 'Mine', label: 'Mine' },
    { value: 'Team', label: 'For the team' },
    { value: 'Everyone', label: "Everyone's" },
  ];
  let status = $state<TaskStatusFilter>('Open');
  let scope = $state<Scope>('Mine');
  const tasks = createListTasks(() => ({ status, scope: 'Everyone' }));
  const team = createListTeamMembers();
  const currentUserId = $derived(page.data.user?.id);
  const editable = $derived(can(page.data.user, 'CompleteTasks'));
  const completion = createTaskCompletion(() => currentUserId);
  // A ticked task keeps its place for its 10 seconds of Undo, though the refetched open list no longer has it.
  const everyone = $derived.by(() => {
    const rows = completion.withHeld(tasks.data?.data ?? []);
    return status === 'Open' ? rows.toSorted((a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id)) : rows;
  });
  const inScope = (task: TaskListItem, which: Scope) =>
    which === 'Everyone' || (which === 'Mine' ? task.assigneeUserId === currentUserId && currentUserId !== undefined : task.assigneeUserId === null);
  const shown = $derived(everyone.filter((task) => inScope(task, scope)));
  // Counts leave out the ticked tasks an open list still shows.
  const counted = $derived(status === 'Open' ? everyone.filter((task) => !task.completedAt) : everyone);
  const countOf = (which: Scope) => counted.filter((task) => inScope(task, which)).length;
  const panelTitle = $derived(status === 'Open' ? 'Open tasks' : 'Done');
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
      <Panel title={panelTitle} flush>
        <!-- The filters, in a row under the header because the list has no search (section 7). Each group stays on one
             line (F5); in a narrow container the two groups stack, so every choice shows at a glance (section 11). -->
        <div class="@container/filters overflow-x-auto border-b border-border-subtle px-4.5 py-2 max-md:px-4">
          <div class="flex flex-col items-start gap-2 @md/filters:flex-row @md/filters:items-center @md/filters:gap-4">
            <div role="group" aria-label="Status" class="flex shrink-0 gap-1">
              <Toggle size="lg" bind:pressed={() => status === 'Open', () => (status = 'Open')}>Open</Toggle>
              <Toggle size="lg" bind:pressed={() => status === 'Completed', () => (status = 'Completed')}>Done</Toggle>
            </div>
            <div role="group" aria-label="Whose" class="flex shrink-0 gap-1">
              {#each scopes as option (option.value)}
                <Toggle size="lg" bind:pressed={() => scope === option.value, () => (scope = option.value)}>
                  {option.label}
                  {#if tasks.isSuccess}<Badge variant="count">{countOf(option.value)}</Badge>{/if}
                </Toggle>
              {/each}
            </div>
          </div>
        </div>

        {#if tasks.isError}
          <div class="p-4">
            <QueryError message={describeError(tasks.error, 'Could not load tasks')} onretry={() => tasks.refetch()} inset />
          </div>
        {:else if tasks.isPending}
          <!-- Rows of the list's shape: an icon, what to do and its facts (E1). -->
          <div aria-hidden="true" class="flex flex-col gap-4 p-4">
            {#each [0, 1, 2] as row (row)}
              <div class="flex gap-3">
                <Skeleton class="size-7 shrink-0 rounded-full" />
                <div class="flex min-w-0 flex-1 flex-col gap-1.5 pt-1">
                  <Skeleton class="h-4 w-3/4" />
                  <Skeleton class="h-5 w-48" />
                </div>
              </div>
            {/each}
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
            {#if scope !== 'Everyone' && countOf('Everyone') > 0}
              <Empty.Content><Button variant="outline" size="lg" onclick={() => (scope = 'Everyone')}>Show everyone's</Button></Empty.Content>
            {/if}
          </Empty.Root>
        {:else}
          <div class="flex flex-col gap-3 px-4.5 py-4 max-md:px-4">
            <TaskList
              label={panelTitle}
              tasks={shown}
              members={team.data?.data}
              {currentUserId}
              oncomplete={editable ? (task) => completion.complete(task) : undefined}
              onundo={(taskId) => completion.undo(taskId)}
              undoable={(taskId) => completion.undoable(taskId)}
              showCandidate
            />
            <FailureText message={completion.failure} />
          </div>
        {/if}
      </Panel>
    </div>
  </div>
  <!-- Outside the list, which leaves when its last open task is done. -->
  <p role="status" class="sr-only">{completion.announcement}</p>
</main>
