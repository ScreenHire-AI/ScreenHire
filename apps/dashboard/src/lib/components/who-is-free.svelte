<script lang="ts">
  import type { FreeTime, InterviewerFreeness } from '@screenhire/api-client';
  import { createListInterviewersFree } from '@screenhire/api-client/svelte-query';
  import type { Snippet } from 'svelte';
  import { formatDateTime } from '#lib/dates.js';

  // Who is free at each of a few times (docs/architecture/interview-scheduling.md), for the team choosing among the
  // candidate's times or picking its own: free, busy, outside their own hours, or unknown where nothing says, in words. It
  // says only free or busy, never what anyone is busy with. A wait between changes keeps it to one question while someone types.
  let {
    jobId,
    interviewerUserIds,
    starts,
    durationMinutes,
    interviewId,
    nameOf,
    label = 'Times',
    row,
  }: {
    jobId: string;
    interviewerUserIds: readonly string[];
    /** ISO instants. */
    starts: readonly string[];
    durationMinutes: number;
    /** The interview being moved, whose own time does not count as busy. */
    interviewId?: string;
    nameOf: (userId: string) => string;
    label?: string;
    /** What goes at the end of a time's row, such as its Book button. */
    row?: Snippet<[string]>;
  } = $props();

  const free = createListInterviewersFree();
  let answers = $state<FreeTime[] | undefined>();
  let asked = 0;

  // Asked again only when the question changes.
  const key = $derived(JSON.stringify({ jobId, interviewerUserIds, starts, durationMinutes, interviewId }));

  $effect(() => {
    const body = JSON.parse(key) as { jobId: string; interviewerUserIds: string[]; starts: string[]; durationMinutes: number; interviewId?: string };
    answers = undefined;
    if (body.interviewerUserIds.length === 0 || body.starts.length === 0) return;
    const ask = ++asked;
    const timer = setTimeout(async () => {
      try {
        const response = await free.mutateAsync({ data: body });
        if (ask === asked) answers = response.data;
      } catch {
        // Who is free is a help, not a gate: the times stay, without it.
      }
    }, 300);
    return () => clearTimeout(timer);
  });

  const words: Record<InterviewerFreeness, string> = { Free: 'Free', Busy: 'Busy', OutsideHours: 'Outside their hours', Unknown: 'No calendar' };

  // "Free: Avery, Robin. Busy: Sam." Each state that someone is in, in the order above.
  function line(start: string): string | undefined {
    const at = answers?.find((answer) => Date.parse(answer.start) === Date.parse(start));
    if (!at) return undefined;
    return (Object.keys(words) as InterviewerFreeness[])
      .map((state) => [state, at.interviewers.filter((person) => person.state === state).map((person) => nameOf(person.userId))] as const)
      .filter(([, names]) => names.length > 0)
      .map(([state, names]) => `${words[state]}: ${names.join(', ')}.`)
      .join(' ');
  }
</script>

<ul aria-label={label} class="flex flex-col gap-3">
  {#each starts as start (start)}
    <li class="flex min-w-0 flex-wrap items-center justify-between gap-x-4 gap-y-1">
      <span class="flex min-w-0 flex-col">
        <time datetime={start} class="font-semibold">{formatDateTime(start)}</time>
        <span class="min-w-0 break-words text-muted-foreground" aria-live="polite">{line(start) ?? ''}</span>
      </span>
      {#if row}{@render row(start)}{/if}
    </li>
  {/each}
</ul>
