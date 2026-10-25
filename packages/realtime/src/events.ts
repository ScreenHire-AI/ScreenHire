import { z } from 'zod';

const id = z.uuid();
const instant = z.iso.datetime({ offset: true });

/**
 * One Zod schema per tenant event the API pushes, keyed by the C# record's name (the event's `type` on the wire).
 * Properties are the record's, camelCased. `contract/events.json`, written by the .NET contract test, holds a sample of
 * every event, and `events.test.ts` parses each one here, so a C# change that is not mirrored fails a test.
 */
export const realtimeEventSchemas = {
  ApplicationMoved: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    fromStageId: z.string(),
    toStageId: z.string(),
    eventId: id,
    movedAt: instant,
  }),
  // An email went to an application's candidate and is on its timeline.
  ApplicationEmailed: z.object({ organizationId: id, applicationId: id, jobId: id, eventId: id, sentAt: instant }),
  // A note from the hiring team: ids only, the dashboard reads the note through GET /v1/applications/{id}/timeline.
  ApplicationNoteAdded: z.object({ organizationId: id, applicationId: id, jobId: id, eventId: id, addedAt: instant }),
  // A note's author changed its text, or someone deleted it (and its text): the timeline refetches.
  ApplicationNoteEdited: z.object({ organizationId: id, applicationId: id, jobId: id, noteId: id, editedAt: instant }),
  ApplicationNoteDeleted: z.object({ organizationId: id, applicationId: id, jobId: id, noteId: id, deletedAt: instant }),
  // A note mentioned a teammate; only their dashboard shows a card. authorUserId is null when an API key wrote it.
  TeammateMentioned: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    noteId: id,
    mentionId: id,
    mentionedUserId: id,
    authorUserId: id.nullable(),
    mentionedAt: instant,
  }),
  ApplicationSubmitted: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    stageId: z.string(),
    eventId: id,
    submittedAt: instant,
  }),
  // A person or an API key added or removed a tag; lists that filter by tag refetch.
  ApplicationTagsChanged: z.object({ organizationId: id, applicationId: id, jobId: id, eventId: id, changedAt: instant }),
  ScorecardSubmitted: z.object({ organizationId: id, applicationId: id, jobId: id, scorecardId: id, eventId: id, submittedAt: instant }),
  QuestionnaireAnswered: z.object({ organizationId: id, applicationId: id, jobId: id, requestId: id, eventId: id, answeredAt: instant }),
  ApplicationTaskCompleted: z.object({ organizationId: id, taskId: id, applicationId: id, completedAt: instant }),
  // Raised for Completed, Failed and Blocked runs; the enum is the C# AutomationRunStatus.
  AutomationRunFinished: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    runId: id,
    automationId: z.string(),
    status: z.enum(['NotMatched', 'Skipped', 'Blocked', 'Running', 'Completed', 'Failed']),
    finishedAt: instant,
  }),
  HiringTemplateVersionPublished: z.object({
    organizationId: id,
    templateId: id,
    versionId: id,
    version: z.number().int(),
    publishedAt: instant,
  }),
  // Levels only, never evidence: the dashboard reads the assessment itself through the API. Enums are the C#
  // AuthenticityLevel and AiAssistanceLevel.
  IntegrityAssessed: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    authenticity: z.enum(['NoConcerns', 'ReviewSuggested', 'VerificationRecommended']),
    aiAssistance: z.enum(['NotIndicated', 'SomeIndicators', 'StrongIndicators']),
    rulesetVersion: z.string(),
    eventId: id,
    assessedAt: instant,
  }),
  JobClosed: z.object({ organizationId: id, jobId: id, closedAt: instant }),
  // A job's postings changed (docs/architecture/job-distribution.md): the dashboard refetches GET /v1/jobs/{jobId}/postings.
  JobPostingsChanged: z.object({ organizationId: id, jobId: id, changedAt: instant }),
  // version and jobVersionId came after the first JobPublished; an envelope queued before them carries version 1 and null.
  JobPublished: z.object({
    organizationId: id,
    jobId: id,
    publishedAt: instant,
    version: z.number().int(),
    jobVersionId: id.nullable(),
  }),
  // A job's own workflow was edited or took a template update (workflow-engine.md §7): its stages may have changed.
  JobWorkflowChanged: z.object({ organizationId: id, jobId: id, workflowVersion: z.number().int(), changedAt: instant }),
  LiveUpdatesChecked: z.object({ organizationId: id, checkId: id }),
  TeamNotified: z.object({ organizationId: id, applicationId: id, jobId: id, runId: id, notifiedAt: instant }),
} as const;

export type RealtimeEventType = keyof typeof realtimeEventSchemas;

/** A parsed event: its type and its data, typed by that type's schema. */
export type RealtimeEvent = {
  [Type in RealtimeEventType]: { type: Type; data: z.infer<(typeof realtimeEventSchemas)[Type]> };
}[RealtimeEventType];

const envelope = z.object({ type: z.string(), data: z.unknown() });

function isEventType(type: string): type is RealtimeEventType {
  return Object.hasOwn(realtimeEventSchemas, type);
}

/**
 * Parses what the hub sent (`{ type, data }`). Returns null for an event type this client does not know, such as one a
 * newer API added, so an old tab ignores it instead of failing. Throws when a known type's data does not match its
 * schema: that is a contract bug, not something to skip quietly.
 */
export function parseRealtimeEvent(raw: unknown): RealtimeEvent | null {
  const { type, data } = envelope.parse(raw);
  if (!isEventType(type)) {
    return null;
  }

  return { type, data: realtimeEventSchemas[type].parse(data) } as RealtimeEvent;
}
