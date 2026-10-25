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
 * `allowed` says whether the person's role has Move candidates: without it every move control is left out (A3) and a
 * drag does nothing, while the API refuses the move anyway (ADR-0014). Call during component initialisation.
 */
export function createMoves(options: { allowed?: () => boolean } = {}) {
  const queryClient = useQueryClient();
  const mutation = createMoveApplication(() => ({
    mutation: {
      onSettled: () =>
        Promise.all([jobsPath, applicationsPath, candidatesPath].map((path) => invalidatePath(queryClient, path))),
    },
  }));

  let announcement = $state('');
  let failure = $state('');

  const moves = {
    /** Whether the person may move candidates; the move controls show only then. */
    get allowed() {
      return options.allowed?.() ?? true;
    },
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

    /** Moves one application; `note` is kept on the move's entry in its Activity. */
    async move(application: Pick<ApplicationSummary, 'id' | 'jobId' | 'candidateName'>, to: WorkflowStage, note?: string): Promise<boolean> {
      failure = '';
      const stage: ApplicationStage = { id: to.id, name: to.name, kind: to.kind };
      const queryKey = getListJobApplicationsQueryKey(application.jobId);
      // A refetch already in flight (another dashboard's ApplicationMoved) would land after this and put the card back.
      await queryClient.cancelQueries({ queryKey });
      queryClient.setQueryData<JobApplications>(queryKey, (cached) =>
        cached && { ...cached, data: cached.data.map((item) => (item.id === application.id ? { ...item, stage } : item)) },
      );
      try {
        await mutation.mutateAsync({ applicationId: application.id, data: { toStageId: to.id, ...(note?.trim() ? { note: note.trim() } : {}) } });
        announcement = `Moved ${application.candidateName} to ${to.name}.`;
        return true;
      } catch (error) {
        failure = describeError(error, `Could not move ${application.candidateName}`);
        announcement = failure;
        return false;
      }
    },

    /**
     * Moves several applications one after another and announces the outcome of all of them, not just the last.
     * Returns the ids of the ones that did not move, so a caller can keep them selected for another try.
     */
    async moveAll(applications: readonly Pick<ApplicationSummary, 'id' | 'jobId' | 'candidateName'>[], to: WorkflowStage): Promise<string[]> {
      const failed: Pick<ApplicationSummary, 'id' | 'candidateName'>[] = [];
      let lastFailure = '';
      for (const application of applications) {
        if (!(await moves.move(application, to))) {
          failed.push(application);
          lastFailure = failure;
        }
      }

      if (failed.length > 0) {
        const moved = applications.length - failed.length;
        failure = `Moved ${moved} of ${applications.length} to ${to.name}. Could not move ${failed.map((application) => application.candidateName).join(', ')}: ${lastFailure}`;
        announcement = failure;
      } else if (applications.length > 1) {
        announcement = `Moved ${applications.length} candidates to ${to.name}.`;
      }

      return failed.map((application) => application.id);
    },
  };

  return moves;
}

export type Moves = ReturnType<typeof createMoves>;
