<script lang="ts">
  import ListTodoIcon from '@lucide/svelte/icons/list-todo';
  import type { TaskListItem, TaskScope, TaskStatusFilter } from '@screenhire/api-client';
  import { createListTasks, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
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

  // The tasks automations make (AssignReviewer, CreateTask, QueueForRejectionReview), on the list template. Mine is the
  // tasks assigned to the person, For the team the tasks assigned to nobody in particular, and Everyone's every task the
  // person can see. One list of everyone's open tasks gives all three and their counts, split the way the API's
  // TaskScope splits them. Done tasks are each scope's newest 200 (TaskEndpoints), so each scope asks for its own: split
  // from everyone's, a busy team's newest 200 would hide the person's older ones. New tasks arrive live with
  // AutomationRunFinished, and completions with ApplicationTaskCompleted and ApplicationTaskReopened (#lib/realtime.ts).
  // A task is ticked off where its work is done, on the candidate's Next steps, so each row here links there.
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
  // The panel is Tasks whichever toggle is pressed, so its title does not repeat the toggle (W4); the list says which.
  const listLabel = $derived(status === 'Open' ? 'Open tasks' : 'Done tasks');
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
             glance, stack one per line (section 11, Toggle). -->
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
            {#if scope !== 'Everyone' && (countOf('Everyone') ?? 0) > 0}
              <Empty.Content><Button variant="outline" size="lg" onclick={() => (scope = 'Everyone')}>Show everyone's</Button></Empty.Content>
            {/if}
          </Empty.Root>
        {:else}
          <div class="flex flex-col gap-3 px-4.5 py-4 max-md:px-4">
            <TaskList
              label={listLabel}
              tasks={shown}
              members={team.data?.data}
              {currentUserId}
              href={(task) => `/applications/${task.applicationId}`}
              showCandidate
            />
          </div>
        {/if}
      </Panel>
    </div>
  </div>
</main>
