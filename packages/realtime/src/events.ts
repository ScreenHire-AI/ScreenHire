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
  ApplicationSubmitted: z.object({
    organizationId: id,
    applicationId: id,
    jobId: id,
    stageId: z.string(),
    eventId: id,
    submittedAt: instant,
  }),
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
  JobClosed: z.object({ organizationId: id, jobId: id, closedAt: instant }),
  // version and jobVersionId came after the first JobPublished; an envelope queued before them carries version 1 and null.
  JobPublished: z.object({
    organizationId: id,
    jobId: id,
    publishedAt: instant,
    version: z.number().int(),
    jobVersionId: id.nullable(),
  }),
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
