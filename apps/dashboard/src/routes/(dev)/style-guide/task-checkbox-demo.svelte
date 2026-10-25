<script lang="ts">
  import type { TeamMember } from '@screenhire/api-client';
  import { onDestroy } from 'svelte';
  import { SvelteMap } from 'svelte/reactivity';
  import TaskList, { type TaskRow } from '#lib/components/task-list.svelte';
  import { undoMilliseconds } from '#lib/tasks.svelte.js';

  // Tasks ticked in place: each stays ticked with Undo for 10 seconds, then reads as done. Nothing is saved.
  let { tasks, members }: { tasks: readonly TaskRow[]; members: readonly TeamMember[] } = $props();

  const ticked = new SvelteMap<string, { at: string; timer?: ReturnType<typeof setTimeout> }>();
  onDestroy(() => {
    for (const entry of ticked.values()) clearTimeout(entry.timer);
  });

  const rows = $derived(
    tasks.map((task) => {
      const entry = ticked.get(task.id);
      return entry ? { ...task, completedAt: entry.at, completedByUserId: 'u1' } : task;
    }),
  );

  function complete(task: TaskRow) {
    const at = new Date().toISOString();
    const timer = setTimeout(() => ticked.set(task.id, { at }), undoMilliseconds);
    ticked.set(task.id, { at, timer });
  }

  function undo(taskId: string) {
    clearTimeout(ticked.get(taskId)?.timer);
    ticked.delete(taskId);
  }
</script>

<TaskList
  label="Tasks"
  tasks={rows}
  {members}
  currentUserId="u1"
  oncomplete={complete}
  onundo={undo}
  undoable={(taskId) => ticked.get(taskId)?.timer !== undefined}
/>
