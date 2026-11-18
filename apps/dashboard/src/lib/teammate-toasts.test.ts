import type { ApplicationDetail, MemberNotificationSettings, TeamMember } from '@screenhire/api-client';
import type { RealtimeEvent } from '@screenhire/realtime';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { appliedWindow, defaultNotificationSettings, type Toast } from './teammate-changes.js';
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

const applied = (n: number, actorUserId: string | null = null): RealtimeEvent => ({
  type: 'ApplicationSubmitted',
  data: { organizationId, applicationId: applicationId(n), jobId: job, stageId: 'applied', eventId: applicationId(n), submittedAt: at, actorUserId },
});

const noteAdded: RealtimeEvent = {
  type: 'ApplicationNoteAdded',
  data: { organizationId, applicationId: applicationId(1), jobId: job, eventId: applicationId(9), addedAt: at, actorUserId: jordan },
};

const mentioned: RealtimeEvent = {
  type: 'TeammateMentioned',
  data: { organizationId, applicationId: applicationId(1), jobId: job, noteId: applicationId(9), mentionId: applicationId(8), mentionedUserId: me, authorUserId: jordan, mentionedAt: at },
};

const emailEdited = (previousEditorUserId: string | null): RealtimeEvent => ({
  type: 'TemplateChanged',
  data: { organizationId, kind: 'EmailTemplate', templateId: 'stage-change', changedAt: at, actorUserId: jordan, previousEditorUserId },
});

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

  it("names the edited template by reading it by its kind, and shows several edits of it in a moment as one toast", async () => {
    const source = { ...sources(), templateName: vi.fn(async () => 'Stage change') };
    const toasts = new TeammateToasts(source, (toast) => shown.push(toast));
    await toasts.receive(emailEdited(me));
    await toasts.receive(emailEdited(me));
    await vi.advanceTimersByTimeAsync(groupWindow);

    expect(shown.map((toast) => toast.sentence.join(''))).toEqual(['Jordan Ellis changed the email template Stage change']);
    expect(source.templateName).toHaveBeenCalledWith('EmailTemplate', 'stage-change');

    await toasts.receive(emailEdited(jordan));
    await vi.advanceTimersByTimeAsync(groupWindow);
    expect(shown).toHaveLength(1);
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

  describe('new applications', () => {
    const texts = () => shown.map((toast) => toast.sentence.join(''));

    it('toasts one application as the candidate applying, without a member to name', async () => {
      const toasts = new TeammateToasts(sources(), (toast) => shown.push(toast));
      await toasts.receive(applied(1));
      await vi.advanceTimersByTimeAsync(groupWindow);

      expect(texts()).toEqual(['Taylor Brook applied to Platform Engineer']);
    });

    it('turns applications that arrive together into one count, reading no candidate', async () => {
      const source = sources();
      const toasts = new TeammateToasts(source, (toast) => shown.push(toast));
      for (let n = 1; n <= 3; n++) await toasts.receive(applied(n));
      await vi.advanceTimersByTimeAsync(groupWindow);

      expect(texts()).toEqual(['3 new applications to Platform Engineer']);
      expect(shown[0]!.href).toBe(`/candidates?view=not-screened&job=${job}`);
      expect(source.application).not.toHaveBeenCalled();
    });

    it('shows the first at once and folds the ones that follow within a minute into the same toast', async () => {
      const toasts = new TeammateToasts(sources(), (toast) => shown.push(toast));
      await toasts.receive(applied(1));
      await vi.advanceTimersByTimeAsync(groupWindow);
      await vi.advanceTimersByTimeAsync(20_000);
      await toasts.receive(applied(2));
      await vi.advanceTimersByTimeAsync(groupWindow);
      await vi.advanceTimersByTimeAsync(20_000);
      await toasts.receive(applied(3));
      await vi.advanceTimersByTimeAsync(groupWindow);

      expect(texts()).toEqual(['Taylor Brook applied to Platform Engineer', '2 new applications to Platform Engineer', '3 new applications to Platform Engineer']);
      expect(new Set(shown.map((toast) => toast.id)).size).toBe(1);
    });

    it('starts a new toast once the minute is over', async () => {
      const toasts = new TeammateToasts(sources(), (toast) => shown.push(toast));
      await toasts.receive(applied(1));
      await vi.advanceTimersByTimeAsync(appliedWindow + groupWindow);
      await toasts.receive(applied(2));
      await vi.advanceTimersByTimeAsync(groupWindow);

      expect(texts()).toEqual(['Taylor Brook applied to Platform Engineer', 'Taylor Brook applied to Platform Engineer']);
      expect(new Set(shown.map((toast) => toast.id)).size).toBe(2);
    });

    it('never goes back to an older count when the first application is read after the next one counted', async () => {
      const source = sources();
      let answer!: (detail: ApplicationDetail) => void;
      source.application.mockImplementationOnce(() => new Promise<ApplicationDetail>((resolve) => (answer = resolve)));
      const toasts = new TeammateToasts(source, (toast) => shown.push(toast));
      await toasts.receive(applied(1));
      await vi.advanceTimersByTimeAsync(groupWindow);
      await toasts.receive(applied(2));
      await vi.advanceTimersByTimeAsync(groupWindow);
      answer({ id: applicationId(1), candidate: { fullName: 'Taylor Brook' }, jobTitle: 'Platform Engineer', stage: { id: 'applied', name: 'Applied' } } as unknown as ApplicationDetail);
      await vi.advanceTimersByTimeAsync(0);

      expect(texts()).toEqual(['2 new applications to Platform Engineer']);
    });

    it('still counts an application that could not be read, without showing it', async () => {
      const source = sources();
      source.application.mockRejectedValueOnce(new Error('gone'));
      const toasts = new TeammateToasts(source, (toast) => shown.push(toast));
      await toasts.receive(applied(1));
      await vi.advanceTimersByTimeAsync(groupWindow);
      expect(texts()).toEqual([]);

      await toasts.receive(applied(2));
      await vi.advanceTimersByTimeAsync(groupWindow);
      expect(texts()).toEqual(['2 new applications to Platform Engineer']);
    });

    it("keeps a member's own additions apart: they name the member", async () => {
      const toasts = new TeammateToasts(sources(), (toast) => shown.push(toast));
      await toasts.receive(applied(1, jordan));
      await vi.advanceTimersByTimeAsync(groupWindow);

      expect(texts()).toEqual(['Jordan Ellis added Taylor Brook']);
    });

    it('stops its timers when disposed', async () => {
      const toasts = new TeammateToasts(sources(), (toast) => shown.push(toast));
      await toasts.receive(applied(1));
      await vi.advanceTimersByTimeAsync(groupWindow);
      toasts.dispose();

      expect(vi.getTimerCount()).toBe(0);
    });
  });
});
