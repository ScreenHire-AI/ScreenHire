import { createCompleteTask, createReopenTask } from '@screenhire/api-client/svelte-query';
import { useQueryClient } from '@tanstack/svelte-query';
import { SvelteMap } from 'svelte/reactivity';
import { onDestroy } from 'svelte';
import { describeError, invalidatePath } from '#lib/query.js';
import { applicationsPath, tasksPath } from '#lib/realtime.js';

/** How long a ticked task keeps its Undo (design system section 11, Task checkbox). */
export const undoMilliseconds = 10_000;

interface Completable {
  id: string;
  note: string;
  completedAt: string | null;
  completedByUserId: string | null;
}

/**
 * Completing tasks, as every task list does it: My tasks and an application's Next steps. Ticking a task marks it done
 * on the server at once, so leaving the page keeps it done, and the row stays in its list, ticked, with Undo for 10
 * seconds; Undo opens it again on the server. Afterwards both lists refetch; other dashboards get it through
 * ApplicationTaskCompleted and ApplicationTaskReopened. Neither changes anything about the application. Call during
 * component initialisation.
 */
export function createTaskCompletion(currentUserId: () => string | undefined) {
  const queryClient = useQueryClient();
  const refetch = () => Promise.all([tasksPath, applicationsPath].map((path) => invalidatePath(queryClient, path)));
  const completeTask = createCompleteTask(() => ({ mutation: { onSettled: refetch } }));
  const reopenTask = createReopenTask(() => ({ mutation: { onSettled: refetch } }));

  // The tasks ticked in the last 10 seconds, as the list should draw them, with the completion on its way or done. An
  // entry being undone draws the task open again, without Undo, until the lists have refetched it.
  const held = new SvelteMap<string, { task: Completable; saved: Promise<boolean>; timer?: ReturnType<typeof setTimeout> }>();
  let announcement = $state('');
  let failure = $state('');

  onDestroy(() => {
    for (const entry of held.values()) clearTimeout(entry.timer);
  });

  function fail(message: string) {
    failure = message;
    announcement = message;
  }

  return {
    /** What the last completion or Undo did, for a polite live region. */
    get announcement() {
      return announcement;
    },
    /** Why the last completion or Undo failed, or empty. */
    get failure() {
      return failure;
    },

    /** Whether the task was ticked in the last 10 seconds, so its row offers Undo. */
    undoable(taskId: string): boolean {
      return held.get(taskId)?.timer !== undefined;
    },

    /**
     * The rows a list draws: each ticked task as done, and a ticked task the refetched list no longer has (an open list)
     * kept where it was until its Undo runs out.
     */
    withHeld<T extends Completable>(rows: readonly T[]): T[] {
      const ids = new Set(rows.map((row) => row.id));
      const kept = [...held.values()].filter((entry) => !ids.has(entry.task.id)).map((entry) => entry.task as T);
      return [...rows.map((row) => (held.get(row.id)?.task as T | undefined) ?? row), ...kept];
    },

    complete(task: Completable): void {
      failure = '';
      const done = { ...task, completedAt: new Date().toISOString(), completedByUserId: currentUserId() ?? null };
      const saved = completeTask
        .mutateAsync({ taskId: task.id })
        .then(() => {
          announcement = `Marked "${task.note}" done.`;
          return true;
        })
        .catch((error: unknown) => {
          clearTimeout(held.get(task.id)?.timer);
          held.delete(task.id);
          fail(describeError(error, 'Could not complete the task'));
          return false;
        });
      held.set(task.id, { task: done, saved, timer: setTimeout(() => held.delete(task.id), undoMilliseconds) });
    },

    async undo(taskId: string): Promise<void> {
      const entry = held.get(taskId);
      if (entry?.timer === undefined) return;
      failure = '';
      clearTimeout(entry.timer);
      const open = { ...entry.task, completedAt: null, completedByUserId: null };
      held.set(taskId, { task: open, saved: entry.saved });
      try {
        if (!(await entry.saved)) return;
        await reopenTask.mutateAsync({ taskId });
        announcement = `"${entry.task.note}" is open again.`;
      } catch (error) {
        fail(describeError(error, 'Could not undo'));
      } finally {
        held.delete(taskId);
      }
    },
  };
}
