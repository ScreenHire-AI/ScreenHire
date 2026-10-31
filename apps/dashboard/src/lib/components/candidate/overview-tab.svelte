<script lang="ts">
  import type { ApplicationDetail, ApplicationTimelineEntry, Automation, Permission, Question, TeamMember } from '@screenhire/api-client';
  import { createGetApplicationIntegrity, createListApplicationTasks } from '@screenhire/api-client/svelte-query';
  import ApplicationTags from '#lib/components/application-tags.svelte';
  import ActivityPanel from '#lib/components/candidate/activity-panel.svelte';
  import AnswersPanel from '#lib/components/candidate/answers-panel.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import FileRow from '#lib/components/file-row.svelte';
  import ManualAutomations from '#lib/components/manual-automations.svelte';
  import OtherApplications from '#lib/components/candidate/other-applications.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import TaskList from '#lib/components/task-list.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import type { ActivityNames } from '#lib/activity.js';
  import { authenticityLevels } from '#lib/integrity.js';
  import { describeError } from '#lib/query.js';
  import { createTaskCompletion } from '#lib/tasks.svelte.js';

  // A candidate's Overview (design system section 22): a grid of panels that uses the width. On the left, what needs you
  // now (Next steps: the application's tasks and, when the integrity level needs a look, its suggested next step) and the
  // last five things anyone did with a box for a note; on the right, the files, the tags and the answers. One column when
  // the page is narrow. The integrity step links to its reasons and is never a reason to reject on its own (ADR-0004).
  let {
    detail,
    timeline,
    timelineError,
    onretrytimeline,
    members,
    mentionable,
    currentUserId,
    permissions,
    names,
    questions,
    manualAutomations,
    tagsEditable,
  }: {
    detail: ApplicationDetail;
    timeline: readonly ApplicationTimelineEntry[] | undefined;
    timelineError?: unknown;
    onretrytimeline: () => void;
    members: readonly TeamMember[] | undefined;
    /** The teammates a note may mention (ActivityPanel). */
    mentionable?: readonly TeamMember[];
    currentUserId: string | undefined;
    /** What the person's role allows; each control shows only with the permission its route needs (ADR-0014). */
    permissions: readonly Permission[];
    names: ActivityNames;
    questions: ReadonlyMap<string, Question>;
    manualAutomations: readonly Automation[];
    /** The person may add and remove tags. */
    tagsEditable: boolean;
  } = $props();

  // AutomationRunFinished and task completions refetch them live (#lib/realtime.ts).
  const tasks = createListApplicationTasks(() => detail.id);
  // Shared with the Integrity tab: one query.
  const integrity = createGetApplicationIntegrity(() => detail.id);
  const completion = createTaskCompletion(() => currentUserId);

  const authenticity = $derived(integrity.data?.data.authenticity);
  const level = $derived(authenticity ? authenticityLevels[authenticity.level] : undefined);
  const integrityStep = $derived(authenticity && level && level.rank > 0 && !authenticity.dismissal ? { view: authenticity, level } : undefined);
  const taskRows = $derived(completion.withHeld(tasks.data?.data.toReversed() ?? []));
</script>

<div class="grid items-start gap-5 @5xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
<div class="flex min-w-0 flex-col gap-5">
<Panel title="Next steps">
  <div class="flex flex-col gap-4">
    {#if tasks.isError}
      <QueryError message={describeError(tasks.error, 'Could not load the tasks')} onretry={() => tasks.refetch()} inset />
    {:else if tasks.isPending}
      <!-- Rows of the list's shape while it loads (E1). -->
      <div aria-hidden="true" class="flex gap-3">
        <Skeleton class="size-7 shrink-0 rounded-full" />
        <div class="flex min-w-0 flex-1 flex-col gap-1.5 pt-1">
          <Skeleton class="h-4 w-3/4" />
          <Skeleton class="h-5 w-32" />
        </div>
      </div>
    {:else if taskRows.length > 0}
      <TaskList
        label="Tasks"
        tasks={taskRows}
        {members}
        {currentUserId}
        oncomplete={permissions.includes('CompleteTasks') ? (task) => completion.complete(task) : undefined}
        onundo={(taskId) => completion.undo(taskId)}
        undoable={(taskId) => completion.undoable(taskId)}
      />
      <FailureText message={completion.failure} />
    {/if}
    {#if integrityStep}
      <!-- One row, as a task is: the level's icon, the step, who suggested it, and the link to the reasons on the right.
           The caveat shows once: the Verification step already says it, the Review step does not (ADR-0004). -->
      <div class="flex items-start gap-3 border-t border-border-subtle pt-3" data-integrity-step>
        <span class="grid size-7 shrink-0 place-items-center"><StatusIcon status={integrityStep.level.status} class="size-5" /></span>
        <div class="flex min-w-0 flex-1 flex-col gap-0.5 pt-1">
          <p class="max-w-measure font-semibold">{integrityStep.view.nextStep}</p>
          <p class="max-w-measure text-muted-foreground">
            {integrityStep.level.label} by the checks.{#if !/reject/i.test(integrityStep.view.nextStep)}{' '}Not a reason to reject on its own.{/if}
          </p>
        </div>
        <a href="?tab=verification" class="inline-flex min-h-6 shrink-0 items-center pt-1 font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11">See the reasons</a>
      </div>
    {/if}
    {#if !tasks.isPending && !tasks.isError && taskRows.length === 0 && !integrityStep}
      <p class="text-muted-foreground">Nothing needs you right now.</p>
    {/if}
    <p role="status" class="sr-only">{completion.announcement}</p>
  </div>
</Panel>

{#if permissions.includes('RunAutomations') && manualAutomations.length > 0}
  <ManualAutomations applicationId={detail.id} automations={manualAutomations} />
{/if}

<ActivityPanel
  applicationId={detail.id}
  entries={timeline}
  error={timelineError}
  onretry={onretrytimeline}
  {members}
  {mentionable}
  {currentUserId}
  {permissions}
  {names}
  limit={5}
  allHref="?tab=activity"
/>
</div>

<div class="flex min-w-0 flex-col gap-5">
<!-- A file opens in the full-window viewer from its row; the page never embeds one (section 9). -->
<Panel title="Files" count={detail.files.length}>
  {#each detail.files as file (file.id)}
    <FileRow applicationId={detail.id} {file} description={`${detail.candidate.fullName}, ${detail.jobTitle}`} />
  {:else}
    <p class="text-muted-foreground">No files.</p>
  {/each}
</Panel>
<ApplicationTags applicationId={detail.id} tags={detail.tags} editable={tagsEditable} />
<AnswersPanel {detail} {questions} fullHref="?tab=application" />
<OtherApplications candidateId={detail.candidate.id} applicationId={detail.id} />
</div>
</div>
