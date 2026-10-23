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
    candidateName?: string;
    jobTitle?: string;
  }
</script>

<script lang="ts">
  import CheckIcon from '@lucide/svelte/icons/check';
  import CircleCheckIcon from '@lucide/svelte/icons/circle-check';
  import ListTodoIcon from '@lucide/svelte/icons/list-todo';
  import type { TeamMember } from '@screenhire/api-client';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { assigneeText, personName, taskKindLabels } from '#lib/team.js';

  // Tasks automations made (design system section 11): an icon in a circle, what to do, its kind and who it is for as
  // fact pills, and time ago; with `showCandidate`, the candidate's name links to the application. With `oncomplete`
  // (createTaskCompletion in #lib/tasks.svelte.ts), an open task has Complete, which marks it done and changes nothing
  // else. Without it (a Viewer, A3) there is no Complete.
  let {
    tasks,
    members,
    currentUserId,
    oncomplete,
    busy = false,
    showCandidate = false,
    label,
  }: {
    tasks: readonly TaskRow[];
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    oncomplete?: (task: TaskRow) => void;
    /** A completion is on its way. */
    busy?: boolean;
    showCandidate?: boolean;
    label: string;
  } = $props();
</script>

<ol aria-label={label} class="flex flex-col">
  {#each tasks as task (task.id)}
    {@const done = Boolean(task.completedAt)}
    <li class="flex items-start gap-3 border-b border-border-subtle py-3 first:pt-0 last:border-b-0 last:pb-0">
      <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground">
        {#if done}<CircleCheckIcon />{:else}<ListTodoIcon />{/if}
      </span>
      <div class="flex min-w-0 flex-1 flex-col gap-1.5 pt-1">
        <p class="break-words">{task.note}</p>
        {#if showCandidate && task.candidateName}
          <p class="flex min-w-0"><TruncatedText text={task.candidateName} href={`/applications/${task.applicationId}`} class="font-semibold" /></p>
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
      {#if !done && oncomplete}
        <Button variant="outline" class="shrink-0" disabled={busy} aria-label={`Complete, ${task.note}`} onclick={() => oncomplete(task)}>
          <CheckIcon aria-hidden="true" />Complete
        </Button>
      {/if}
    </li>
  {/each}
</ol>
