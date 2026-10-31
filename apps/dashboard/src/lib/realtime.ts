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
export const membersPath = '/v1/organization/members';

/**
 * Which queries each tenant event makes stale, as API paths. Add a line when a page shows what an event changes.
 * `/v1/jobs` covers the jobs list, each job (its postings and applicant count) and each job's applications.
 * HiringTemplateVersionPublished is here because the jobs list shows "template update available" and the templates
 * pages show each template's versions; an automation run can tag an application, which its page shows.
 * IntegrityAssessed changes the chips in every list, the job's overview and the application's integrity panel
 * (`/v1/applications/{id}/integrity`). An automation run can also create a task, which My tasks (`/v1/tasks`) and the
 * application's own tasks show, and so can a person or an API key (ApplicationTaskCreated), which Activity shows too.
 * Tags show in every list, which filter by them. Every list also shows each application's last activity
 * (`lastActivityAt`), which anything the candidate or the team does moves: a note and its edits and deletion, a sent
 * email (ApplicationEmailed), answers to a follow-up questionnaire (QuestionnaireAnswered), a task, an automation run.
 * So those refetch the lists too, the job's own table (under `/v1/jobs`) and a candidate's applications included.
 * TeammateMentioned refreshes nothing: the note's own event does, and the shell turns the mention into a card for the
 * person mentioned.
 */
export const invalidations: InvalidationMap = {
  JobPublished: [jobsPath],
  JobClosed: [jobsPath],
  JobPostingsChanged: [jobsPath],
  // A changed time limit changes when applications fall overdue (slaDueAt), so the lists refetch too.
  JobWorkflowChanged: [jobsPath, applicationsPath, candidatesPath],
  // A job's team (`/v1/jobs/{id}/hiring-team`), the Team page's job counts, and for someone just added, the job itself.
  HiringTeamChanged: [jobsPath, membersPath],
  HiringTemplateVersionPublished: [jobsPath, templatesPath],
  ApplicationSubmitted: [jobsPath, applicationsPath, candidatesPath],
  ApplicationMoved: [jobsPath, applicationsPath, candidatesPath],
  // The application's Activity, the candidate's Messages (every email to them) and every list's last activity.
  ApplicationEmailed: [jobsPath, applicationsPath, candidatesPath],
  ApplicationNoteAdded: [jobsPath, applicationsPath, candidatesPath],
  ApplicationNoteEdited: [jobsPath, applicationsPath, candidatesPath],
  ApplicationNoteDeleted: [jobsPath, applicationsPath, candidatesPath],
  ApplicationTagsChanged: [jobsPath, applicationsPath, candidatesPath],
  // A scorecard changes the candidate's rating in the lists; a finished review task can show it to its reviewer.
  ScorecardSubmitted: [jobsPath, applicationsPath, candidatesPath],
  QuestionnaireAnswered: [jobsPath, applicationsPath, candidatesPath],
  ApplicationTaskCreated: [jobsPath, applicationsPath, candidatesPath, tasksPath],
  ApplicationTaskCompleted: [jobsPath, applicationsPath, candidatesPath, tasksPath],
  ApplicationTaskReopened: [jobsPath, applicationsPath, candidatesPath, tasksPath],
  AutomationRunFinished: [jobsPath, applicationsPath, candidatesPath, tasksPath],
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
