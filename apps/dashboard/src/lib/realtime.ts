import {
  createConnectionState,
  invalidateOnEvents,
  RealtimeClient,
  type ConnectionState,
  type InvalidationMap,
  type RealtimeEvent,
} from '@screenhire/realtime';
import type { QueryClient } from '@tanstack/svelte-query';
import { invalidatePath } from '#lib/query.js';

/** API paths whose queries live events make stale; `invalidatePath` covers every query under each. */
export const jobsPath = '/v1/jobs';
export const applicationsPath = '/v1/applications';
export const candidatesPath = '/v1/candidates';
export const templatesPath = '/v1/templates';
export const tasksPath = '/v1/tasks';

/**
 * Which queries each tenant event makes stale, as API paths. Add a line when a page shows what an event changes.
 * `/v1/jobs` covers the jobs list, each job (its postings and applicant count) and each job's applications.
 * HiringTemplateVersionPublished is here because the jobs list shows "template update available" and the templates
 * pages show each template's versions; an automation run can tag an application, which its page shows.
 * IntegrityAssessed changes the chips in every list, the job's overview and the application's integrity panel
 * (`/v1/applications/{id}/integrity`). An automation run can also create a task, which My tasks (`/v1/tasks`) and the
 * application's own tasks show. Tags show in every list, which filter by them; a note shows on the application's
 * timeline only.
 */
export const invalidations: InvalidationMap = {
  JobPublished: [jobsPath],
  JobClosed: [jobsPath],
  JobPostingsChanged: [jobsPath],
  JobWorkflowChanged: [jobsPath],
  HiringTemplateVersionPublished: [jobsPath, templatesPath],
  ApplicationSubmitted: [jobsPath, applicationsPath, candidatesPath],
  ApplicationMoved: [jobsPath, applicationsPath, candidatesPath],
  ApplicationNoteAdded: [applicationsPath],
  ApplicationTagsChanged: [jobsPath, applicationsPath, candidatesPath],
  ScorecardSubmitted: [applicationsPath],
  ApplicationTaskCompleted: [applicationsPath, tasksPath],
  AutomationRunFinished: [applicationsPath, tasksPath],
  IntegrityAssessed: [jobsPath, applicationsPath, candidatesPath],
};

export interface LiveUpdates {
  /** The connection's status, as runes state, for the shell's indicator. */
  readonly connection: ConnectionState;
  /** Receives one record's events (`topic` such as `job`) until the returned function is called. */
  subscribe(topic: string, id: string): () => void;
  /** Calls `handler` with every event, for what is more than a refetch (a notification). Returns a function that removes it. */
  onEvent(handler: (event: RealtimeEvent) => void): () => void;
  stop(): Promise<void>;
}

/**
 * Connects the signed-in shell to the API's real-time hub. Events mark the queries that show what changed as stale,
 * so the ones on screen refetch, and a reconnect does that for all of them, so the screen catches up on anything it
 * missed.
 */
export function startLiveUpdates(queryClient: QueryClient): LiveUpdates {
  const client = new RealtimeClient();
  const connection = createConnectionState(client);
  const stopInvalidating = invalidateOnEvents(client, invalidations, (path) => invalidatePath(queryClient, path));
  void client.start();

  return {
    connection,
    subscribe: (topic, id) => client.subscribe(topic, id),
    onEvent: (handler) => client.onEvent(handler),
    async stop() {
      stopInvalidating();
      connection.dispose();
      await client.stop();
    },
  };
}
