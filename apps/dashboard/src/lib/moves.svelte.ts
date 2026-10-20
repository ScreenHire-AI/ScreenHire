import type { ApplicationStage, ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
import { createMoveApplication, getListJobApplicationsQueryKey } from '@screenhire/api-client/svelte-query';
import { useQueryClient } from '@tanstack/svelte-query';
import { describeError, invalidatePath } from '#lib/query.js';
import { applicationsPath, candidatesPath, jobsPath } from '#lib/realtime.js';

type JobApplications = { data: ApplicationSummary[] };

/**
 * Moving applications between stages, as every move control (menu, keyboard, drag) does it. The job's cached list
 * changes at once, so the card or row moves before the API answers; then every list that shows applications refetches,
 * which also undoes the change if the API refused it. Other dashboards get the move through ApplicationMoved.
 * Call during component initialisation.
 */
export function createMoves() {
  const queryClient = useQueryClient();
  const mutation = createMoveApplication(() => ({
    mutation: {
      onSettled: () =>
        Promise.all([jobsPath, applicationsPath, candidatesPath].map((path) => invalidatePath(queryClient, path))),
    },
  }));

  let announcement = $state('');
  let failure = $state('');

  return {
    /** What the last move did, for a polite live region. */
    get announcement() {
      return announcement;
    },
    /** Why the last move failed, or empty. */
    get failure() {
      return failure;
    },
    get pending() {
      return mutation.isPending;
    },

    async move(application: Pick<ApplicationSummary, 'id' | 'jobId' | 'candidateName'>, to: WorkflowStage): Promise<boolean> {
      failure = '';
      const stage: ApplicationStage = { id: to.id, name: to.name, kind: to.kind };
      queryClient.setQueryData<JobApplications>(getListJobApplicationsQueryKey(application.jobId), (cached) =>
        cached && { ...cached, data: cached.data.map((item) => (item.id === application.id ? { ...item, stage } : item)) },
      );
      try {
        await mutation.mutateAsync({ applicationId: application.id, data: { toStageId: to.id } });
        announcement = `Moved ${application.candidateName} to ${to.name}.`;
        return true;
      } catch (error) {
        failure = describeError(error, `Could not move ${application.candidateName}`);
        announcement = failure;
        return false;
      }
    },
  };
}

export type Moves = ReturnType<typeof createMoves>;
