<script lang="ts">
  import VideoIcon from '~icons/lucide/video';
  import type { ApplicationInterview } from '@screenhire/api-client';
  import FailureText from '#lib/components/failure-text.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { formatDateIn, formatDateTime } from '#lib/dates.js';

  // What the Interview panel says about one interview (docs/architecture/interview-scheduling.md): its status badge, then who
  // interviews and the meeting link while it is booked, the days the candidate can choose from while it waits, or how it
  // ended. The time of a booked interview is the header's one fact and is not said again here (W7). Split from the panel so the
  // style guide can draw each state without the data behind it.
  let { interview, candidateName }: { interview: ApplicationInterview; candidateName?: string } = $props();

  const candidate = $derived(candidateName ?? 'the candidate');

  const firstName = (name: string) => name.split(' ')[0] ?? name;
  // A window's last day: its end is the start of the day after.
  const lastDay = (value: ApplicationInterview) => formatDateIn(new Date(Date.parse(value.windowEnd) - 1).toISOString(), value.timeZone);

  /** How a cancelled or expired interview ended, in one line. */
  function ended(value: ApplicationInterview): string {
    if (value.status === 'Expired') return `The candidate did not pick a time before ${lastDay(value)}.`;
    const by = value.cancelledByCandidate ? ' by the candidate' : '';
    return value.startsAt ? `The interview on ${formatDateTime(value.startsAt)} was cancelled${by}.` : `The invitation was cancelled${by}.`;
  }
</script>

{#if interview.status === 'Open'}
  <div class="flex flex-col items-start gap-1.5">
    <StatusBadge status="waiting" label="Waiting for the candidate" />
    <p class="text-muted-foreground">Times from {formatDateIn(interview.windowStart, interview.timeZone)} to {lastDay(interview)}.</p>
  </div>
{:else if interview.status === 'Booked'}
  <div class="flex flex-col items-start gap-4">
    <StatusBadge status="booked" label="Booked" />
    {#if interview.newTimesOfferedAt}
      <!-- The booked time stays held until the candidate picks one of the new times, so this says what is being waited for. -->
      <div class="flex flex-col items-start gap-1.5">
        <StatusBadge status="waiting" label="Waiting for a new time" />
        <p class="text-muted-foreground">
          The booked time stays until {candidate} picks one. New times from {formatDateIn(interview.windowStart, interview.timeZone)} to {lastDay(interview)}.
        </p>
      </div>
    {/if}
    <ul aria-label="Interviewers" class="flex flex-col gap-3">
      {#each interview.interviewers as person (person.userId)}
        <li class="flex min-w-0 flex-col gap-0.5">
          <span class="flex min-w-0 flex-wrap items-center gap-x-2 font-semibold">
            <span class="min-w-0 break-words">{person.displayName}</span>
            {#if person.isOrganizer && interview.interviewers.length > 1}<Badge variant="muted">Organizer</Badge>{/if}
          </span>
          {#if person.syncStatus === 'Failed'}
            <FailureText role="status" message={person.syncProblem ?? `Could not put this on ${firstName(person.displayName)}'s calendar.`} />
          {:else if person.syncStatus === 'Pending'}
            <p class="text-muted-foreground">Not on {firstName(person.displayName)}'s calendar yet.</p>
          {/if}
        </li>
      {/each}
    </ul>
    {#if interview.meetingUrl}
      <a
        href={interview.meetingUrl}
        target="_blank"
        rel="noopener noreferrer"
        class="inline-flex min-h-6 w-fit items-center gap-1.5 font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11"
      >
        <VideoIcon aria-hidden="true" class="size-4" />Join the meeting<span class="sr-only"> (opens in a new tab)</span>
      </a>
    {/if}
  </div>
{:else}
  <div class="flex flex-col items-start gap-1.5">
    <StatusBadge status="notInEffect" label={interview.status === 'Expired' ? 'Expired' : 'Cancelled'} />
    <p class="text-muted-foreground">{ended(interview)}</p>
  </div>
{/if}
