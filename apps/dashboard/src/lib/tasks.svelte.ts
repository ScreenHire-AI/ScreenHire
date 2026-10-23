import { createCompleteTask } from '@screenhire/api-client/svelte-query';
import { useQueryClient } from '@tanstack/svelte-query';
import { describeError, invalidatePath } from '#lib/query.js';
import { applicationsPath, tasksPath } from '#lib/realtime.js';

/**
 * Completing tasks, as every task list does it: My tasks and an application's Tasks panel. Afterwards both lists
 * refetch; other dashboards get it through ApplicationTaskCompleted. Completing changes nothing about the application.
 * Call during component initialisation.
 */
export function createTaskCompletion() {
  const queryClient = useQueryClient();
  const mutation = createCompleteTask(() => ({
    mutation: { onSettled: () => Promise.all([tasksPath, applicationsPath].map((path) => invalidatePath(queryClient, path))) },
  }));

  let announcement = $state('');
  let failure = $state('');

  return {
    /** What the last completion did, for a polite live region. */
    get announcement() {
      return announcement;
    },
    /** Why the last completion failed, or empty. */
    get failure() {
      return failure;
    },
    get pending() {
      return mutation.isPending;
    },

    async complete(task: { id: string; note: string }): Promise<void> {
      failure = '';
      try {
        await mutation.mutateAsync({ taskId: task.id });
        announcement = `Marked "${task.note}" done.`;
      } catch (error) {
        failure = describeError(error, 'Could not complete the task');
        announcement = failure;
      }
    },
  };
}
