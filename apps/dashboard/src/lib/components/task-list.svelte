<script lang="ts" module>
  import type { ApplicationTaskKind } from '@screenhire/api-client';

  /** A task as the list draws it: an application's own task, or one from My tasks with its candidate and job. */
  export interface TaskRow {
    id: string;
    applicationId: string;
    kind: ApplicationTaskKind;
    note: string;
    assigneeUserId: string | null;
    createdAt: string;
    completedAt: string | null;
    completedByUserId: string | null;
    /** The library scorecard a Scorecard task asks for. */
    scorecardTemplateId?: string | null;
    candidateName?: string;
    jobTitle?: string;
  }
</script>

<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import ListTodoIcon from '~icons/lucide/list-todo';
  import Undo2Icon from '~icons/lucide/undo-2';
  import type { TeamMember } from '@screenhire/api-client';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Checkbox } from '#lib/components/ui/checkbox/index.js';
  import { completesByHand } from '#lib/tasks.svelte.js';
  import { assigneeText, personName, taskKindLabels } from '#lib/team.js';

  // Tasks automations made (design system section 11): an icon in a circle, what to do, its kind and who it is for as
  // fact pills, and time ago; with `showCandidate`, the candidate's name links to the application. With `oncomplete`
  // (createTaskCompletion in #lib/tasks.svelte.ts), an open task has the task checkbox in the icon's place: ticking it
  // marks the task done and changes nothing else, and for 10 seconds (`undoable`) the row stays ticked with Undo, as
  // does unticking it. Without it (a Viewer, A3) there is no checkbox. Only a to-do and a rejection review are ticked: a
  // review and a requested scorecard are done when the scorecard is submitted (ADR-0017 decision 11), so `work` links
  // such a row to where that happens, on the right, as Next steps' other links sit.
  let {
    tasks,
    members,
    currentUserId,
    oncomplete,
    onundo,
    undoable = () => false,
    showCandidate = false,
    href,
    work,
    label,
  }: {
    tasks: readonly TaskRow[];
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    oncomplete?: (task: TaskRow) => void;
    onundo?: (taskId: string) => void;
    /** Whether the task was just ticked and still offers Undo. */
    undoable?: (taskId: string) => boolean;
    showCandidate?: boolean;
    /**
     * Where the task's work is done. The whole row then links there, and the task is ticked off in that place, not here
     * (My tasks): a list of things to do takes you to each one.
     */
    href?: (task: TaskRow) => string;
    /** A link to where an open task that is not ticked by hand gets done, when the reader can do it there. */
    work?: (task: TaskRow) => { href: string; label: string } | undefined;
    label: string;
  } = $props();

  // On My tasks the same note repeats across candidates, so the name says whose task it is (X1).
  const describe = (task: TaskRow) => (showCandidate && task.candidateName ? `${task.candidateName}: ${task.note}` : task.note);
</script>

<ol aria-label={label} class="flex flex-col">
  {#each tasks as task (task.id)}
    {@const done = Boolean(task.completedAt)}
    {@const undo = Boolean(onundo) && undoable(task.id)}
    {@const link = !done && !href ? work?.(task) : undefined}
    <!-- In a narrow container (the 320px side column) Undo goes under the facts, so the text and pills keep the row's
         width (F2). -->
    <li class={['@container/task relative border-b border-border-subtle py-3 first:pt-0 last:border-b-0 last:pb-0', href && 'rounded-md hover:bg-secondary/60']}>
      <div class="grid grid-cols-[auto_minmax(0,1fr)] items-start gap-x-3 gap-y-2 @md/task:grid-cols-[auto_minmax(0,1fr)_auto]">
        {#if (!done && oncomplete && completesByHand(task.kind)) || undo}
          <!-- The task checkbox, centred in the icon's 28px place. Its hit area reaches 44px on touch (F4). -->
          <span class="grid size-7 shrink-0 place-items-center">
            <!-- A round to-do tick with a 2px edge, so it reads as something to tick before anyone presses it. -->
            <Checkbox
              class="size-5 rounded-full border-2 hover:border-foreground pointer-coarse:after:-inset-3"
              checked={done}
              aria-label={`Mark done for ${describe(task)}`}
              onCheckedChange={(checked) => (checked ? oncomplete?.(task) : onundo?.(task.id))}
            />
          </span>
        {:else}
          <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground">
            {#if done}<CheckIcon />{:else}<ListTodoIcon />{/if}
          </span>
        {/if}
        <!-- The task, then its facts on the same line when they fit, so a list of steps stays compact. -->
        <div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1.5 pt-1">
          {#if href}
            <!-- The row is the link to the work (the whole row is its target), so the candidate's name is plain text. -->
            <a href={href(task)} class="min-w-0 max-w-measure font-semibold break-words text-foreground no-underline after:absolute after:inset-0 hover:underline">{task.note}</a>
            <!-- What to do is the row's one semibold line; whose task it is reads as body text after it (L1). -->
            {#if showCandidate && task.candidateName}<span class="min-w-0 truncate">{task.candidateName}</span>{/if}
          {:else}
            <p class={['min-w-0 max-w-measure font-semibold break-words', undo && 'text-muted-foreground line-through']}>{task.note}</p>
            {#if showCandidate && task.candidateName}
              <p class="flex min-w-0"><TruncatedText text={task.candidateName} href={`/applications/${task.applicationId}`} /></p>
            {/if}
          {/if}
          <div class="flex flex-wrap items-center gap-2">
            {#if showCandidate && task.jobTitle}<Badge variant="muted" class="max-w-60 min-w-0"><TruncatedText text={task.jobTitle} /></Badge>{/if}
            <Badge variant="muted">{taskKindLabels[task.kind]}</Badge>
            <Badge variant="muted">{assigneeText(task.assigneeUserId, members, currentUserId)}</Badge>
            {#if done && task.completedAt}
              <span class="text-caption text-muted-foreground">
                Done <RelativeTime at={task.completedAt} />{task.completedByUserId ? ` by ${personName(task.completedByUserId, members, currentUserId)}` : ''}
              </span>
            {:else}
              <RelativeTime at={task.createdAt} class="text-caption text-muted-foreground" />
            {/if}
          </div>
        </div>
        {#if link}
          <a
            href={link.href}
            aria-label={`${link.label}: ${describe(task)}`}
            class="col-start-2 inline-flex min-h-6 items-center justify-self-start pt-1 font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11 @md/task:col-start-3 @md/task:row-start-1"
            >{link.label}</a
          >
        {/if}
        {#if undo}
          <Button
            variant="outline"
            class="col-start-2 justify-self-start @md/task:col-start-3 @md/task:row-start-1"
            aria-label={`Undo, ${describe(task)}`}
            onclick={() => onundo?.(task.id)}
          >
            <Undo2Icon aria-hidden="true" />Undo
          </Button>
        {/if}
      </div>
    </li>
  {/each}
</ol>
