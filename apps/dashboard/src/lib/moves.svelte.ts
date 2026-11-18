import type { ApplicationStage, ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
import {
  createMoveApplication,
  getGetJobQueryOptions,
  getListEmailTemplatesQueryOptions,
  getListJobApplicationsQueryKey,
  getListTeamMembersQueryOptions,
} from '@screenhire/api-client/svelte-query';
import { useQueryClient } from '@tanstack/svelte-query';
import { tick } from 'svelte';
import { enteringLines } from '#lib/automation-sentence.js';
import { preferences } from '#lib/preferences.svelte.js';
import { describeError, invalidatePath } from '#lib/query.js';
import { applicationsPath, candidatesPath, jobsPath } from '#lib/realtime.js';
import { getShell } from '#lib/shell.js';
import type { Toast } from '#lib/teammate-changes.js';
import { emailTemplateNames } from '#lib/workflow.js';

type JobApplications = { data: ApplicationSummary[] };
/** What a move reads of an application: its job's title, when the caller has it, words the toast. */
type Movable = Pick<ApplicationSummary, 'id' | 'jobId' | 'candidateName'> & { jobTitle?: string };

/** What the confirmation dialog shows while a move waits for the person's answer. */
export interface MoveConfirmation {
  title: string;
  /** The sentence before the list, said once: "Moving them there will:" or "Each of them gets:". */
  intro: string;
  /** What entering the stage does, one sentence each (`enteringLines`). */
  lines: readonly string[];
  /** The element that had focus when the question opened, which gets it back after Cancel. */
  restoreFocusTo: HTMLElement | null;
}

/**
 * Moving applications between stages, as every move control (menu, keyboard, drag) does it. The job's cached list
 * changes at once, so the card or row moves before the API answers; then every list that shows applications refetches,
 * which also undoes the change if the API refused it. Other dashboards get the move through ApplicationMoved.
 * `allowed` says whether the person's role has Move candidates: without it every move control is left out (A3) and a
 * drag does nothing, while the API refuses the move anyway (ADR-0014). Call during component initialisation.
 * A move that worked shows as a toast in the shell's tray, as a teammate's does, with the job under it; the tray is a live
 * region, so it is also what a screen reader hears. Without a shell (a test harness) the announcement says it instead.
 * A failure stays in `failure`, on the page, beside what failed.
 * Entering a stage that has automations asks first (`confirmation`, drawn by MoveConfirmDialog): the dialog lists what the
 * stage does, from the job's template, in the Automations map's words. A stage with none moves at once, and so does every
 * stage once the person ticks "Don't ask again" (`preferences.confirmMoves`). A cancelled move returns false and does nothing.
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
  const shell = getShell();
  let toasts = 0;
  function done(sentence: Toast['sentence'], href: string, job: string | undefined, fallback: string) {
    if (!shell?.toast) {
      announcement = fallback;
      return;
    }
    announcement = '';
    shell.toast({ id: `own-move:${++toasts}:${Date.now()}`, kind: 'Moves', sentence, href, context: job ?? '' });
  }

  let confirmation = $state<MoveConfirmation | null>(null);
  let answerQuestion: ((moved: boolean) => void) | undefined;
  // What had the focus when the person answered Move, which gets it back once the move is done (`refocus`).
  let afterMove: HTMLElement | null = null;

  // Set from the moment a move starts asking until the person answers, so a second move (two quick drops) cannot take the
  // question over and leave the first one waiting for ever.
  let asking = false;

  /**
   * What entering `to` does for these applications' jobs, in words. It reads the job from the cache when the page holds
   * it (its query refetches when a teammate edits the template) and fetches it otherwise, and it fetches people and email
   * names only once a stage has something to say. Empty when nothing runs there, or when the job cannot be read: a move is
   * never held up by a failed read.
   */
  async function entering(applications: readonly Movable[], to: WorkflowStage): Promise<string[]> {
    try {
      const jobIds = [...new Set(applications.map((application) => application.jobId))];
      const workflows = await Promise.all(jobIds.map(async (jobId) => (await queryClient.ensureQueryData(getGetJobQueryOptions(jobId))).data.workflow));
      const runsHere = workflows.filter((workflow) => enteringLines(workflow, to.id).length > 0);
      if (runsHere.length === 0) return [];
      const [members, templates] = await Promise.allSettled([
        queryClient.ensureQueryData(getListTeamMembersQueryOptions()),
        queryClient.ensureQueryData(getListEmailTemplatesQueryOptions()),
      ]);
      const names = emailTemplateNames(templates.status === 'fulfilled' ? templates.value.data.templates : undefined);
      const lines = new Set<string>();
      for (const workflow of runsHere) {
        for (const line of enteringLines(workflow, to.id, members.status === 'fulfilled' ? members.value.data : undefined, names)) lines.add(line);
      }
      return [...lines];
    } catch {
      return [];
    }
  }

  /**
   * What gets the focus back once the question is answered: the focused element, or, when that is an item in a menu (Move to
   * stage), the button that opened the menu, because the item is gone once the menu closes.
   */
  function opener(): HTMLElement | null {
    const active = globalThis.document?.activeElement;
    if (!(active instanceof HTMLElement) || active === document.body) return null;
    const menu = active.closest('[role="menu"]');
    return (menu?.id && document.querySelector<HTMLElement>(`[aria-controls="${menu.id}"]`)) || active;
  }

  /** Asks, when the stage has something to say. True when the move may go ahead; false when cancelled or another question is open. */
  async function ask(applications: readonly Movable[], to: WorkflowStage): Promise<boolean> {
    if (asking) return false;
    if (!preferences.confirmMoves) return true;
    asking = true;
    try {
      const lines = await entering(applications, to);
      if (lines.length === 0) return true;
      const one = applications.length === 1;
      confirmation = {
        title: one ? `Move ${applications[0]!.candidateName} to ${to.name}?` : `Move ${applications.length} candidates to ${to.name}?`,
        intro: one ? 'Moving them there will:' : 'Each of them gets:',
        lines,
        restoreFocusTo: opener(),
      };
      return await new Promise<boolean>((resolve) => {
        answerQuestion = resolve;
      });
    } finally {
      asking = false;
    }
  }

  const moves = {
    /** The question a move is waiting on, or null; MoveConfirmDialog shows it. */
    get confirmation() {
      return confirmation;
    },
    /** The person's answer to the question: Move (`true`, with whether to stop asking) or Cancel (`false`). */
    answer(moved: boolean, dontAskAgain = false) {
      if (moved && dontAskAgain) preferences.setConfirmMoves(false);
      afterMove = moved ? (confirmation?.restoreFocusTo ?? null) : null;
      const resolve = answerQuestion;
      answerQuestion = undefined;
      confirmation = null;
      resolve?.(moved);
    },

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

    /** Moves one application, after asking if its stage does something; `note` is kept on the move's entry in its Activity. */
    async move(application: Movable, to: WorkflowStage, note?: string): Promise<boolean> {
      failure = '';
      if (!(await ask([application], to))) return false;
      const moved = await perform(application, to, note);
      if (afterMove) await refocus(afterMove);
      return moved;
    },

    /**
     * Moves several applications one after another and announces the outcome of all of them, not just the last. Asks once
     * for all of them. Returns the ids of the ones that did not move (all of them when cancelled), so a caller can keep them
     * selected for another try.
     */
    async moveAll(applications: readonly Movable[], to: WorkflowStage): Promise<string[]> {
      failure = '';
      if (applications.length === 0) return [];
      if (!(await ask(applications, to))) return applications.map((application) => application.id);
      const failed = await performAll(applications, to);
      if (afterMove) await refocus(afterMove);
      return failed;
    },
  };

  /**
   * After a move the person said yes to, the focus goes back to what asked, once the move is done: a Move to stage button
   * is disabled while its move runs, which drops the focus to the page. A control the move took away is left alone, and so
   * is a focus something else took (the board puts it on the moved card).
   */
  async function refocus(back: HTMLElement): Promise<void> {
    afterMove = null;
    await tick();
    // The button comes back on once the mutation's state reaches the page, which can be a task after the move resolves.
    if (back.matches(':disabled')) await enabled(back);
    const active = document.activeElement;
    if ((active && active !== document.body) || !back.isConnected || back.matches(':disabled')) return;
    back.focus();
  }

  /** Resolves once `element` is no longer disabled, or after two seconds. */
  function enabled(element: HTMLElement): Promise<void> {
    return new Promise((resolve) => {
      const done = () => (observer.disconnect(), clearTimeout(timer), resolve());
      const observer = new MutationObserver(() => {
        if (!element.matches(':disabled')) done();
      });
      const timer = setTimeout(done, 2000);
      observer.observe(element, { attributes: true, attributeFilter: ['disabled'] });
    });
  }

  async function perform(application: Movable, to: WorkflowStage, note?: string): Promise<boolean> {
    const stage: ApplicationStage = { id: to.id, name: to.name, kind: to.kind };
    const queryKey = getListJobApplicationsQueryKey(application.jobId);
    // A refetch already in flight (another dashboard's ApplicationMoved) would land after this and put the card back.
    await queryClient.cancelQueries({ queryKey });
    queryClient.setQueryData<JobApplications>(queryKey, (cached) =>
      cached && { ...cached, data: cached.data.map((item) => (item.id === application.id ? { ...item, stage, keptInMindAt: null } : item)) },
    );
    try {
      await mutation.mutateAsync({ applicationId: application.id, data: { toStageId: to.id, ...(note?.trim() ? { note: note.trim() } : {}) } });
      done(['You moved ', application.candidateName, ` to ${to.name}`], `/applications/${application.id}`, application.jobTitle, `Moved ${application.candidateName} to ${to.name}.`);
      return true;
    } catch (error) {
      failure = describeError(error, `Could not move ${application.candidateName}`);
      announcement = failure;
      return false;
    }
  }

  async function performAll(applications: readonly Movable[], to: WorkflowStage): Promise<string[]> {
    const failed: Pick<ApplicationSummary, 'id' | 'candidateName'>[] = [];
    let lastFailure = '';
    for (const application of applications) {
      if (!(await perform(application, to))) {
        failed.push(application);
        lastFailure = failure;
      }
    }

    if (failed.length > 0) {
      const moved = applications.length - failed.length;
      failure = `Moved ${moved} of ${applications.length} to ${to.name}. Could not move ${failed.map((application) => application.candidateName).join(', ')}: ${lastFailure}`;
      announcement = failure;
    } else if (applications.length > 1) {
      const first = applications[0]!;
      done(['You moved ', `${applications.length} candidates`, ` to ${to.name}`], `/jobs/${first.jobId}`, first.jobTitle, `Moved ${applications.length} candidates to ${to.name}.`);
    }

    return failed.map((application) => application.id);
  }

  return moves;
}

export type Moves = ReturnType<typeof createMoves>;
