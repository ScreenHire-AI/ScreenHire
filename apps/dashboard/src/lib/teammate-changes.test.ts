import type { RealtimeEvent } from '@screenhire/realtime';
import { describe, expect, it } from 'vitest';
import { changeFor, defaultNotificationSettings, groupKey, groupToastFor, toastFor, type ChangeContext } from './teammate-changes.js';

const me = '0199f0c4-0000-7000-8000-00000000000a';
const jordan = '0199f0c4-0000-7000-8000-00000000000b';
const teamJob = '0199f0c4-0000-7000-8000-000000000101';
const otherJob = '0199f0c4-0000-7000-8000-000000000102';
const at = '2026-10-30T09:00:00+00:00';
const ids = { organizationId: '0199f0c4-0000-7000-8000-000000000001', applicationId: '0199f0c4-0000-7000-8000-000000000201', eventId: '0199f0c4-0000-7000-8000-000000000301' };

const context: ChangeContext = { userId: me, teamJobIds: new Set([teamJob]), settings: defaultNotificationSettings };

const moved = (actorUserId: string | null, jobId = teamJob): RealtimeEvent => ({
  type: 'ApplicationMoved',
  data: { ...ids, jobId, fromStageId: 'applied', toStageId: 'interview', movedAt: at, actorUserId },
});

const task = (type: 'ApplicationTaskCreated' | 'ApplicationTaskCompleted', assigneeUserId: string | null): RealtimeEvent =>
  ({
    type,
    data: { organizationId: ids.organizationId, taskId: ids.eventId, applicationId: ids.applicationId, jobId: otherJob, actorUserId: jordan, assigneeUserId, createdAt: at, completedAt: at },
  }) as RealtimeEvent;

const published = (previousEditorUserId: string | null): RealtimeEvent => ({
  type: 'HiringTemplateVersionPublished',
  data: { organizationId: ids.organizationId, templateId: ids.applicationId, versionId: ids.eventId, version: 3, publishedAt: at, actorUserId: jordan, previousEditorUserId },
});

describe('which teammates changes toast', () => {
  it("toasts a teammate's move of a candidate on a job whose hiring team the person is on", () => {
    expect(changeFor(moved(jordan), context)).toEqual({ kind: 'Moves', actorUserId: jordan, applicationId: ids.applicationId, jobId: teamJob });
  });

  it("never toasts the person's own change, or one no member made (an API key, an automation)", () => {
    expect(changeFor(moved(me), context)).toBeUndefined();
    expect(changeFor(moved(null), context)).toBeUndefined();
  });

  it('needs the hiring team: seeing a job is not enough', () => {
    expect(changeFor(moved(jordan, otherJob), context)).toBeUndefined();
  });

  it('never toasts a verification result, whatever its level', () => {
    const assessed: RealtimeEvent = {
      type: 'IntegrityAssessed',
      data: { ...ids, jobId: teamJob, authenticity: 'VerificationRecommended', aiAssistance: 'StrongIndicators', rulesetVersion: '1', assessedAt: at },
    };
    expect(changeFor(assessed, context)).toBeUndefined();
  });

  it('leaves out a kind turned off, and everything while toasts are off', () => {
    expect(changeFor(moved(jordan), { ...context, settings: { ...defaultNotificationSettings, off: ['Moves'] } })).toBeUndefined();
    expect(changeFor(moved(jordan), { ...context, settings: { ...defaultNotificationSettings, toasts: false } })).toBeUndefined();
    expect(changeFor(moved(jordan), { ...context, settings: { ...defaultNotificationSettings, off: ['Notes'] } })?.kind).toBe('Moves');
  });

  it('toasts a task given to the person on any job they see, and not one for someone else or the team', () => {
    expect(changeFor(task('ApplicationTaskCreated', me), context)).toMatchObject({ kind: 'Tasks', action: 'created' });
    expect(changeFor(task('ApplicationTaskCompleted', me), context)).toMatchObject({ kind: 'Tasks', action: 'completed' });
    expect(changeFor(task('ApplicationTaskCreated', jordan), context)).toBeUndefined();
    expect(changeFor(task('ApplicationTaskCreated', null), context)).toBeUndefined();
  });

  it('toasts a published template only to the person who changed it last before the publisher', () => {
    expect(changeFor(published(me), context)).toEqual({ kind: 'Templates', actorUserId: jordan, templateId: ids.applicationId, version: 3 });
    expect(changeFor(published(null), context)).toBeUndefined();
  });

  it('groups by kind, person and job, and keeps tasks and templates apart', () => {
    const first = changeFor(moved(jordan), context)!;
    expect(groupKey(first)).toBe(`Moves:${jordan}:${teamJob}`);
    expect(groupKey(changeFor(published(me), context)!)).toBe(`Templates:${ids.applicationId}:3`);
  });
});

describe('toast words', () => {
  it('says who did what, with the candidate as the link and the job under it', () => {
    const toast = toastFor('t-1', changeFor(moved(jordan), context)!, {
      actorName: 'Jordan Ellis',
      candidateName: 'Taylor Brook',
      jobTitle: 'Platform Engineer',
      stageName: 'Interview',
    });
    expect(toast).toEqual({
      id: 't-1',
      kind: 'Moves',
      sentence: ['Jordan Ellis moved ', 'Taylor Brook', ' to Interview'],
      href: `/applications/${ids.applicationId}`,
      context: 'Platform Engineer',
    });
  });

  it("links a task to its candidate, and a template to the template", () => {
    const given = toastFor('t-2', changeFor(task('ApplicationTaskCreated', me), context)!, {
      actorName: 'Jordan Ellis',
      candidateName: 'Taylor Brook',
      jobTitle: 'Platform Engineer',
      taskNote: 'Call references',
    });
    expect(given.sentence).toEqual(['Jordan Ellis gave you a task on ', 'Taylor Brook', ': Call references']);
    expect(given.context).toBe('Platform Engineer');

    const template = toastFor('t-3', changeFor(published(me), context)!, { actorName: 'Jordan Ellis', templateName: 'Software engineer' });
    expect(template.sentence.join('')).toBe('Jordan Ellis published version 3 of Software engineer');
    expect(template.href).toBe(`/templates/${ids.applicationId}`);
  });

  it('words a group as one change on the job, linking to the job', () => {
    const toast = groupToastFor('t-4', 'Emails', teamJob, 12, 'Jordan Ellis', 'Platform Engineer');
    expect(toast.sentence).toEqual(['Jordan Ellis emailed 12 candidates on ', 'Platform Engineer', '']);
    expect(toast.href).toBe(`/jobs/${teamJob}`);
  });
});
