import {
  createConnectionState,
  invalidateOnEvents,
  RealtimeClient,
  type ConnectionState,
  type InvalidationMap,
  type RealtimeEvent,
} from '@screenhire/realtime';
import type { QueryClient } from '@tanstack/svelte-query';
import { emailTemplatesPath } from '#lib/email.js';
import { invalidatePath } from '#lib/query.js';
import { scorecardTemplatesPath, templateUsagePath } from '#lib/templates.js';

/** API paths whose queries live events make stale; `invalidatePath` covers every query under each. */
export const jobsPath = '/v1/jobs';
export const applicationsPath = '/v1/applications';
export const candidatesPath = '/v1/candidates';
export const templatesPath = '/v1/templates';
export const tasksPath = '/v1/tasks';
export const membersPath = '/v1/organization/members';
export const talentPoolPath = '/v1/talent-pool';
export const reportsPath = '/v1/reports';
export const viewsPath = '/v1/views';
export const dataRequestsPath = '/v1/data-requests';
/** The plan, trial and open job count (`/v1/organization/billing`), also the shell's answer to whether billing is set up. */
export const billingPath = '/v1/organization/billing';
/** The retention setting, its dry-run count and its history (`/v1/organization/data-retention`). */
export const retentionPath = '/v1/organization/data-retention';

/**
 * Which queries each tenant event makes stale, as API paths. Add a line when a page shows what an event changes.
 * `/v1/jobs` covers the jobs list, each job (its postings and applicant count) and each job's applications.
 * HiringTemplateVersionPublished is here because the jobs list shows "template update available" and the templates
 * pages show each template's versions; an automation run can tag an application, which its page shows.
 * VerificationAssessed changes the chips in every list, the job's overview and the application's verification panel
 * (`/v1/applications/{id}/verification`). An automation run can also create a task, which My tasks (`/v1/tasks`) and the
 * application's own tasks show, and so can a person or an API key (ApplicationTaskCreated), which Activity shows too.
 * Tags show in every list, which filter by them. Every list also shows each application's last activity
 * (`lastActivityAt`), which anything the candidate or the team does moves: a note and its edits and deletion, a sent
 * email (ApplicationEmailed), answers to a follow-up questionnaire (QuestionnaireAnswered), a task, an automation run.
 * So those refetch the lists too, the job's own table (under `/v1/jobs`) and a candidate's applications included.
 * A candidate's reply (CandidateReplied) is a new line in Activity and Messages and moves the list's last activity, like a
 * sent email. The flag on an address (CandidateEmailStatusChanged) changes the candidate shown beside every application.
 * TeammateMentioned refreshes nothing: the note's own event does, and the shell turns the mention into a card for the
 * person mentioned. Reports count applications by when they arrived and the stages they reached, and list the open jobs
 * a person sees, so arriving, moving, a job opening or closing, a changed workflow and a changed hiring team refresh them.
 * A data request arriving or answered (DataRequestReceived, DataRequestChanged) refreshes the Data requests list and its open
 * count. An erasure blanks the candidate on a request's row and changes the retention dry-run's count, so CandidateErased
 * refreshes both, and the retention panel's Last run when a run erased someone's last application.
 */
export const invalidations: InvalidationMap = {
  // Reports list the open jobs, so opening and closing one refreshes them.
  // The Billing page counts open jobs against the plan's limit, so they refresh it as well.
  JobPublished: [jobsPath, reportsPath, billingPath],
  JobClosed: [jobsPath, reportsPath, billingPath],
  JobPostingsChanged: [jobsPath],
  // A changed time limit changes when applications fall overdue (slaDueAt), so the lists refetch too.
  JobWorkflowChanged: [jobsPath, applicationsPath, candidatesPath, reportsPath],
  // A job's team (`/v1/jobs/{id}/hiring-team`), the Team page's job counts, and for someone just added, the job itself.
  HiringTeamChanged: [jobsPath, membersPath, reportsPath],
  HiringTemplateVersionPublished: [jobsPath, templatesPath],
  // Any template edit, of the three kinds: the Templates page, the pickers on a new job, the email composer and an
  // application's scorecard library read them, and the usage list shows where each is used. Events are mapped by
  // type, not by kind, so all of them refetch.
  TemplateChanged: [templatesPath, emailTemplatesPath, scorecardTemplatesPath, templateUsagePath],
  ApplicationSubmitted: [jobsPath, applicationsPath, candidatesPath, reportsPath],
  ApplicationMoved: [jobsPath, applicationsPath, candidatesPath, reportsPath],
  // The application's Activity, the candidate's Messages (every email to them) and every list's last activity.
  ApplicationEmailed: [jobsPath, applicationsPath, candidatesPath, talentPoolPath],
  CandidateReplied: [jobsPath, applicationsPath, candidatesPath, talentPoolPath],
  CandidateEmailStatusChanged: [applicationsPath, candidatesPath],
  // An interview offered, booked, moved or cancelled: the application's interviews (`/v1/applications/{id}/interviews`),
  // its Activity and every list's last activity.
  InterviewChanged: [jobsPath, applicationsPath, candidatesPath],
  ApplicationNoteAdded: [jobsPath, applicationsPath, candidatesPath],
  ApplicationNoteEdited: [jobsPath, applicationsPath, candidatesPath],
  ApplicationNoteDeleted: [jobsPath, applicationsPath, candidatesPath],
  ApplicationTagsChanged: [jobsPath, applicationsPath, candidatesPath],
  // A scorecard changes the candidate's rating in the lists; a finished review task can show it to its reviewer.
  ScorecardSubmitted: [jobsPath, applicationsPath, candidatesPath, talentPoolPath],
  QuestionnaireAnswered: [jobsPath, applicationsPath, candidatesPath],
  ApplicationTaskCreated: [jobsPath, applicationsPath, candidatesPath, tasksPath],
  ApplicationTaskCompleted: [jobsPath, applicationsPath, candidatesPath, tasksPath],
  ApplicationTaskReopened: [jobsPath, applicationsPath, candidatesPath, tasksPath],
  AutomationRunFinished: [jobsPath, applicationsPath, candidatesPath, tasksPath],
  VerificationAssessed: [jobsPath, applicationsPath, candidatesPath],
  // Keeping someone in mind ends their application in every list; leaving the pool, or an invitation, changes the pool
  // and the kept application's page.
  TalentPoolChanged: [talentPoolPath, jobsPath, applicationsPath, candidatesPath],
  // An erased candidate leaves every list, the pool and the job tables, and an open page of theirs finds nothing.
  CandidateErased: [talentPoolPath, jobsPath, applicationsPath, candidatesPath, tasksPath, reportsPath, dataRequestsPath, retentionPath],
  DataRequestReceived: [dataRequestsPath],
  DataRequestChanged: [dataRequestsPath],
  // Retention removed ended applications, some from candidates who keep another one: every list and count refetches, and
  // the retention panel's Last run and dry-run count change.
  ApplicationsRemoved: [jobsPath, applicationsPath, candidatesPath, tasksPath, reportsPath, retentionPath],
  // A teammate's shared view appears in the sidebar, and a rename or a delete reaches everyone who sees it.
  ViewsChanged: [viewsPath],
  // The subscription changed (checkout done, a failed payment, a plan change or its end): the Billing page and its entry.
  BillingChanged: [billingPath],
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
