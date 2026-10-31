import type { ApplicationDetail, ApplicationTaskEntry, MemberJob, MemberNotificationSettings, TeamMember } from '@screenhire/api-client';
import type { RealtimeEvent } from '@screenhire/realtime';
import {
  changeFor,
  groupKey,
  groupToastFor,
  toastFor,
  type TeammateChange,
  type Toast,
} from '#lib/teammate-changes.js';

/** What the toasts read through the API. Each may throw (a 404, the network); the toast is then dropped. */
export interface TeammateToastSources {
  userId(): string | undefined;
  members(): Promise<readonly TeamMember[]>;
  memberJobs(memberId: string): Promise<readonly MemberJob[]>;
  settings(): Promise<MemberNotificationSettings>;
  application(applicationId: string): Promise<ApplicationDetail>;
  tasks(applicationId: string): Promise<readonly ApplicationTaskEntry[]>;
  templateName(templateId: string): Promise<string>;
}

/** How long changes wait to be grouped (a bulk move arrives as one event per candidate). */
export const groupWindow = 1_000;

interface Pending {
  changes: TeammateChange[];
  timer: ReturnType<typeof setTimeout>;
}

/**
 * Turns teammates' changes in the live events into toasts (docs/architecture/realtime.md, "Teammates' changes as
 * toasts"). Each change waits {@link groupWindow} under its group, before anything is read: one becomes a toast about
 * that candidate, several become one toast about the job, so a bulk move makes one toast and no request per candidate.
 * A note that mentions the person shows only the mention card, which already says it.
 */
export class TeammateToasts {
  readonly #sources: TeammateToastSources;
  readonly #show: (toast: Toast) => void;
  readonly #pending = new Map<string, Pending>();
  // Notes that mentioned this person, by application and author, while they could still be waiting here, each with the
  // timer that forgets it.
  readonly #mentions = new Map<string, ReturnType<typeof setTimeout>>();
  #count = 0;
  #disposed = false;

  constructor(sources: TeammateToastSources, show: (toast: Toast) => void) {
    this.#sources = sources;
    this.#show = show;
  }

  /** Takes every live event; returns once it is decided, for tests. */
  async receive(event: RealtimeEvent): Promise<void> {
    const me = this.#sources.userId();
    if (event.type === 'TeammateMentioned') {
      if (event.data.mentionedUserId === me && event.data.authorUserId) {
        const key = `${event.data.applicationId}:${event.data.authorUserId}`;
        clearTimeout(this.#mentions.get(key));
        this.#mentions.set(key, setTimeout(() => this.#mentions.delete(key), groupWindow * 2));
      }
      return;
    }

    try {
      const change = changeFor(event, await this.#context(me));
      if (change) this.#hold(change);
    } catch {
      // The team or the settings could not be read: the change stays in Activity, so the toast is dropped.
    }
  }

  /** Stops every timer; a change still being read when the shell goes away shows nothing. */
  dispose(): void {
    this.#disposed = true;
    for (const pending of this.#pending.values()) clearTimeout(pending.timer);
    this.#pending.clear();
    for (const timer of this.#mentions.values()) clearTimeout(timer);
    this.#mentions.clear();
  }

  async #context(userId: string | undefined) {
    const [members, settings] = await Promise.all([this.#sources.members(), this.#sources.settings()]);
    const memberId = members.find((member) => member.userId === userId)?.id;
    const jobs = memberId ? await this.#sources.memberJobs(memberId) : [];
    return { userId, teamJobIds: new Set(jobs.map((job) => job.id)), settings };
  }

  #hold(change: TeammateChange): void {
    if (this.#disposed) return;
    const key = groupKey(change);
    const pending = this.#pending.get(key);
    if (pending) {
      pending.changes.push(change);
      return;
    }

    this.#pending.set(key, { changes: [change], timer: setTimeout(() => void this.#flush(key), groupWindow) });
  }

  async #flush(key: string): Promise<void> {
    const changes = (this.#pending.get(key)?.changes ?? []).filter(
      (change) => !(change.kind === 'Notes' && this.#mentions.has(`${change.applicationId}:${change.actorUserId}`)),
    );
    this.#pending.delete(key);
    const first = changes[0];
    if (!first) return;

    try {
      const members = await this.#sources.members();
      const actorName = members.find((member) => member.userId === first.actorUserId)?.displayName ?? 'A teammate';
      const id = `change:${++this.#count}`;
      if (changes.length > 1 && first.kind !== 'Tasks' && first.kind !== 'Templates') {
        const memberId = members.find((member) => member.userId === this.#sources.userId())?.id;
        const jobs = memberId ? await this.#sources.memberJobs(memberId) : [];
        const jobTitle = jobs.find((job) => job.id === first.jobId)?.title;
        if (!this.#disposed) this.#show(groupToastFor(id, first.kind, first.jobId, changes.length, actorName, jobTitle));
        return;
      }

      const facts = await this.#facts(first);
      if (!this.#disposed) this.#show(toastFor(id, first, { actorName, ...facts }));
    } catch {
      // The record is gone or hidden now, or the network failed: the change stays where it happened.
    }
  }

  async #facts(change: TeammateChange) {
    if (change.kind === 'Templates') return { templateName: await this.#sources.templateName(change.templateId) };
    const application = await this.#sources.application(change.applicationId);
    const facts = { candidateName: application.candidate.fullName, jobTitle: application.jobTitle, stageName: application.stage.name };
    if (change.kind !== 'Tasks') return facts;
    const tasks = await this.#sources.tasks(change.applicationId);
    return { ...facts, taskNote: tasks.find((task) => task.id === change.taskId)?.note };
  }
}
