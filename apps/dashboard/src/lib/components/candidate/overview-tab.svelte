<script lang="ts">
  import type { ApplicationDetail, CandidateEmailEntry, Permission, ScorecardEntry, TeamMember } from '@screenhire/api-client';
  import { createGetApplicationVerification, createListApplicationTasks } from '@screenhire/api-client/svelte-query';
  import InterviewPanel from '#lib/components/candidate/interview-panel.svelte';
  import LatestMessagePanel from '#lib/components/candidate/latest-message-panel.svelte';
  import ScorecardsSummary from '#lib/components/candidate/scorecards-summary.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import LevelMark from '#lib/components/level-mark.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TaskList, { type TaskRow } from '#lib/components/task-list.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { overviewStepOf, verificationStateOf } from '#lib/verification.js';
  import { describeError } from '#lib/query.js';
  import { completesByHand, createTaskCompletion, reviewLink, scorecardLink } from '#lib/tasks.svelte.js';

  // A candidate's Overview (design system section 21): four panels, and nothing a tab already holds. On the left, Needs you (the
  // application's tasks and, when the verification state needs a look, its one-sentence step) and the Scorecards given; on the
  // right, the Interview once one is offered and the Latest message. One column when the page is narrow. The verification step
  // links to its reasons and is never a reason to reject on its own (ADR-0004).
  let {
    detail,
    members,
    currentUserId,
    permissions,
    scorecards,
    withheld = false,
    emails,
    onfill,
    oninterviewchanged,
  }: {
    detail: ApplicationDetail;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** What the person's role allows; each control shows only with the permission its route needs (ADR-0014). */
    permissions: readonly Permission[];
    scorecards: readonly ScorecardEntry[] | undefined;
    /** The reader has a scorecard to give: the others show once they submit theirs. */
    withheld?: boolean;
    /** Every email to or from the candidate, newest first; undefined while they load. */
    emails: readonly CandidateEmailEntry[] | undefined;
    onfill?: () => void;
    /** After the interview was changed from its panel (new times offered): a sentence for the page's status line. */
    oninterviewchanged?: (message: string) => void;
  } = $props();

  // AutomationRunFinished and task completions refetch them live (#lib/realtime.ts).
  const tasks = createListApplicationTasks(() => detail.id);
  // Shared with the Verification tab: one query.
  const verification = createGetApplicationVerification(() => detail.id);
  const completion = createTaskCompletion(() => currentUserId);

  const authenticity = $derived(verification.data?.data.authenticity);
  const verificationState = $derived(authenticity ? verificationStateOf(authenticity.level, Boolean(authenticity.dismissal)) : undefined);
  const step = $derived(verificationState ? overviewStepOf(verificationState) : undefined);
  const taskRows = $derived(completion.withHeld(tasks.data?.data.toReversed() ?? []));

  // A review or a requested scorecard is done on the Scorecards tab, by the person it is for (anyone, for a review for
  // the whole team), so their row's Fill in opens the sheet there: on the requested scorecard, or for a review on the
  // scorecard for the stage the candidate is in.
  function scorecardWork(task: TaskRow): { href: string; label: string } | undefined {
    if (completesByHand(task.kind) || (task.assigneeUserId !== null && task.assigneeUserId !== currentUserId)) return undefined;
    return { href: `?${task.kind === 'Scorecard' && task.scorecardTemplateId ? scorecardLink(task.scorecardTemplateId) : reviewLink}`, label: 'Fill in' };
  }
</script>

<div class="grid items-start gap-5 @5xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
  <div class="flex min-w-0 flex-col gap-5">
    <Panel title="Needs you">
      <div class="flex flex-col gap-4">
        {#if tasks.isError}
          <QueryError message={describeError(tasks.error, 'Could not load the tasks')} onretry={() => tasks.refetch()} inset />
        {:else if tasks.isPending}
          <!-- Rows of the list's shape while it loads (E1). -->
          <div aria-hidden="true" class="flex gap-3">
            <Skeleton class="size-7 shrink-0 rounded-full" />
            <div class="flex min-w-0 flex-1 flex-col gap-1.5 pt-1">
              <Skeleton class="h-4 w-3/4" />
            </div>
          </div>
        {:else if taskRows.length > 0}
          <TaskList
            label="Tasks"
            plain
            tasks={taskRows}
            {members}
            {currentUserId}
            oncomplete={permissions.includes('CompleteTasks') ? (task) => completion.complete(task) : undefined}
            onundo={(taskId) => completion.undo(taskId)}
            undoable={(taskId) => completion.undoable(taskId)}
            work={permissions.includes('FillInScorecards') ? scorecardWork : undefined}
          />
          <FailureText message={completion.failure} />
        {/if}
        {#if verificationState && step && (verificationState.kind === 'review' || verificationState.kind === 'verify')}
          <!-- One row, as a task is: the level mark, the step in one sentence, and the link to the reasons on the right (under the
               step in a narrow container, so the text keeps the row's width, F2). The state is the badge beside the name (W7), so
               the row does not say it again, and it never speaks of rejecting (ADR-0004). -->
          <div class={['@container/step flex items-start gap-3', taskRows.length > 0 && 'border-t border-border-subtle pt-3']} data-verification-step>
            <span class="grid size-7 shrink-0 place-items-center"><LevelMark steps={verificationState.steps} tone={verificationState.kind} size="lg" /></span>
            <div class="grid min-w-0 flex-1 items-start gap-x-3 gap-y-1 @md/step:grid-cols-[minmax(0,1fr)_auto]">
              <p class="max-w-measure pt-1 font-semibold">{step}</p>
              <a href="?tab=verification" class="inline-flex min-h-6 items-center justify-self-start pt-1 font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11">See the reasons</a>
            </div>
          </div>
        {:else if verification.isError}
          <!-- Without the verification state the panel cannot say nothing needs the reader, so it says what failed. -->
          <QueryError message={describeError(verification.error, 'Could not load the verification state')} onretry={() => verification.refetch()} inset />
        {/if}
        {#if !tasks.isPending && !tasks.isError && !verification.isPending && !verification.isError && taskRows.length === 0 && !step}
          <p class="text-muted-foreground">Nothing needs you right now.</p>
        {/if}
        <p role="status" class="sr-only">{completion.announcement}</p>
      </div>
    </Panel>

    <ScorecardsSummary rating={detail.rating} {scorecards} {members} {currentUserId} {withheld} {onfill} />
  </div>

  <div class="flex min-w-0 flex-col gap-5">
    <!-- The interview offered to the candidate, once there is one (docs/architecture/interview-scheduling.md). -->
    <InterviewPanel applicationId={detail.id} candidateName={detail.candidate.fullName} mayChange={permissions.includes('EmailCandidates')} ondone={oninterviewchanged} />
    <LatestMessagePanel applicationId={detail.id} {emails} {members} {currentUserId} />
  </div>
</div>
