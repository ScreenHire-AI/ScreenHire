<script lang="ts">
  import ListTodoIcon from '@lucide/svelte/icons/list-todo';
  import type { TaskScope, TaskStatusFilter } from '@screenhire/api-client';
  import { createListTasks, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import { can } from '#lib/permissions.js';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TaskList from '#lib/components/task-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { describeError } from '#lib/query.js';
  import { createTaskCompletion } from '#lib/tasks.svelte.js';

  // The tasks automations make (AssignReviewer, CreateTask, QueueForRejectionReview), on the list template. Mine is the
  // person's own tasks and the whole team's (no assignee); Everyone adds teammates'. New tasks arrive live with
  // AutomationRunFinished, and completions with ApplicationTaskCompleted (#lib/realtime.ts).
  let status = $state<TaskStatusFilter>('Open');
  let scope = $state<TaskScope>('Mine');
  const tasks = createListTasks(() => ({ status, scope }));
  const team = createListTeamMembers();
  const all = $derived(tasks.data?.data ?? []);
  const currentUserId = $derived(page.data.user?.id);
  const editable = $derived(can(page.data.user, 'CompleteTasks'));
  const completion = createTaskCompletion();
  const panelTitle = $derived(status === 'Open' ? 'Open tasks' : 'Done');
</script>

<svelte:head>
  <title>My tasks · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'My tasks' }]} />

<main class="flex min-w-0 flex-col">
  <HeaderBand title="My tasks" count={tasks.isSuccess ? all.length : undefined} countLabel={all.length === 1 ? 'task' : 'tasks'} />

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
              <Toggle size="lg" bind:pressed={() => scope === 'Mine', () => (scope = 'Mine')}>Mine and the team's</Toggle>
              <Toggle size="lg" bind:pressed={() => scope === 'Everyone', () => (scope = 'Everyone')}>Everyone's</Toggle>
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
        {:else if all.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><ListTodoIcon /></Empty.Media>
              <Empty.Title>{status === 'Open' ? 'Nothing to do' : 'Nothing done yet'}</Empty.Title>
              <Empty.Description>
                {status === 'Open' ? 'Automations add tasks here when a workflow asks for a review or a to-do.' : 'Tasks you and your team complete show here.'}
              </Empty.Description>
            </Empty.Header>
            {#if scope === 'Mine'}
              <Empty.Content><Button variant="outline" size="lg" onclick={() => (scope = 'Everyone')}>Show everyone's</Button></Empty.Content>
            {/if}
          </Empty.Root>
        {:else}
          <div class="flex flex-col gap-3 px-4.5 py-4 max-md:px-4">
            <TaskList
              label={panelTitle}
              tasks={all}
              members={team.data?.data}
              {currentUserId}
              oncomplete={editable ? (task) => completion.complete(task) : undefined}
              busy={completion.pending}
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
