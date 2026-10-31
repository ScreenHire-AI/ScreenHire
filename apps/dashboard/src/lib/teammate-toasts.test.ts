import type { ApplicationDetail, MemberNotificationSettings, TeamMember } from '@screenhire/api-client';
import type { RealtimeEvent } from '@screenhire/realtime';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defaultNotificationSettings, type Toast } from './teammate-changes.js';
import { groupWindow, TeammateToasts, type TeammateToastSources } from './teammate-toasts.js';

const me = '0199f0c4-0000-7000-8000-00000000000a';
const jordan = '0199f0c4-0000-7000-8000-00000000000b';
const job = '0199f0c4-0000-7000-8000-000000000101';
const at = '2026-10-30T09:00:00+00:00';
const organizationId = '0199f0c4-0000-7000-8000-000000000001';
const applicationId = (n: number) => `0199f0c4-0000-7000-8000-${String(200 + n).padStart(12, '0')}`;

const members = [
  { id: 'member-me', userId: me, displayName: 'Avery Quinn' },
  { id: 'member-jordan', userId: jordan, displayName: 'Jordan Ellis' },
] as TeamMember[];

function sources(settings: MemberNotificationSettings = defaultNotificationSettings): TeammateToastSources & { application: ReturnType<typeof vi.fn> } {
  return {
    userId: () => me,
    members: async () => members,
    memberJobs: async () => [{ id: job, title: 'Platform Engineer', status: 'Open' as const }],
    settings: async () => settings,
    application: vi.fn(async (id: string) => ({ id, candidate: { fullName: 'Taylor Brook' }, jobTitle: 'Platform Engineer', stage: { id: 'interview', name: 'Interview', kind: 'Interview' } }) as unknown as ApplicationDetail),
    tasks: async () => [],
    templateName: async () => 'Software engineer',
  };
}

const moved = (n: number): RealtimeEvent => ({
  type: 'ApplicationMoved',
  data: { organizationId, applicationId: applicationId(n), jobId: job, fromStageId: 'applied', toStageId: 'interview', eventId: applicationId(n), movedAt: at, actorUserId: jordan },
});

const noteAdded: RealtimeEvent = {
  type: 'ApplicationNoteAdded',
  data: { organizationId, applicationId: applicationId(1), jobId: job, eventId: applicationId(9), addedAt: at, actorUserId: jordan },
};

const mentioned: RealtimeEvent = {
  type: 'TeammateMentioned',
  data: { organizationId, applicationId: applicationId(1), jobId: job, noteId: applicationId(9), mentionId: applicationId(8), mentionedUserId: me, authorUserId: jordan, mentionedAt: at },
};

describe('teammate toasts', () => {
  let shown: Toast[];
  beforeEach(() => {
    vi.useFakeTimers();
    shown = [];
  });
  afterEach(() => vi.useRealTimers());

  it('shows one change as a toast about the candidate once the group window passes', async () => {
    const toasts = new TeammateToasts(sources(), (toast) => shown.push(toast));
    await toasts.receive(moved(1));
    expect(shown).toEqual([]);

    await vi.advanceTimersByTimeAsync(groupWindow);
    expect(shown.map((toast) => toast.sentence.join(''))).toEqual(['Jordan Ellis moved Taylor Brook to Interview']);
  });

  it('turns a bulk move into one toast about the job, reading no candidate', async () => {
    const source = sources();
    const toasts = new TeammateToasts(source, (toast) => shown.push(toast));
    for (let n = 1; n <= 5; n++) await toasts.receive(moved(n));
    await vi.advanceTimersByTimeAsync(groupWindow);

    expect(shown.map((toast) => toast.sentence.join(''))).toEqual(['Jordan Ellis moved 5 candidates on Platform Engineer']);
    expect(source.application).not.toHaveBeenCalled();
  });

  it('leaves a note that mentions the person to the mention card, whichever event comes first', async () => {
    const toasts = new TeammateToasts(sources(), (toast) => shown.push(toast));
    await toasts.receive(noteAdded);
    await toasts.receive(mentioned);
    await vi.advanceTimersByTimeAsync(groupWindow);

    expect(shown).toEqual([]);
  });

  it('reads the settings each time, so a kind turned off stops at once', async () => {
    const toasts = new TeammateToasts(sources({ ...defaultNotificationSettings, off: ['Moves'] }), (toast) => shown.push(toast));
    await toasts.receive(moved(1));
    await vi.advanceTimersByTimeAsync(groupWindow);

    expect(shown).toEqual([]);
  });

  it('drops the toast when the candidate cannot be read any more', async () => {
    const source = sources();
    source.application.mockRejectedValueOnce(new Error('404'));
    const toasts = new TeammateToasts(source, (toast) => shown.push(toast));
    await toasts.receive(moved(1));
    await vi.advanceTimersByTimeAsync(groupWindow);

    expect(shown).toEqual([]);
  });

  it('shows nothing once disposed, waiting changes and changes still being read alike', async () => {
    const source = sources();
    let readCandidate: () => void = () => {};
    source.application.mockImplementationOnce(
      (id: string) =>
        new Promise((resolve) => {
          readCandidate = () => resolve({ id, candidate: { fullName: 'Taylor Brook' }, jobTitle: 'Platform Engineer', stage: { id: 'interview', name: 'Interview', kind: 'Interview' } });
        }),
    );
    const toasts = new TeammateToasts(source, (toast) => shown.push(toast));
    await toasts.receive(moved(1));
    await vi.advanceTimersByTimeAsync(groupWindow);
    await toasts.receive(moved(2));
    await toasts.receive(mentioned);

    toasts.dispose();
    readCandidate();
    await vi.advanceTimersByTimeAsync(groupWindow * 2);
    await toasts.receive(moved(3));
    await vi.advanceTimersByTimeAsync(groupWindow);

    expect(shown).toEqual([]);
    expect(vi.getTimerCount()).toBe(0);
  });
});
