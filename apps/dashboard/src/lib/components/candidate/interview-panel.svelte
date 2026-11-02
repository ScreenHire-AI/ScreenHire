<script lang="ts">
  import CalendarXIcon from '~icons/lucide/calendar-x';
  import VideoIcon from '~icons/lucide/video';
  import type { ApplicationInterview } from '@screenhire/api-client';
  import { createCancelInterview, createListApplicationInterviews } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { formatDateIn, formatDateTime } from '#lib/dates.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';

  // The candidate's interview (docs/architecture/interview-scheduling.md, design system ADR-0017): what is open or booked, or
  // how the last one ended. The time of a booked interview is the header's one fact and is not said again here (W7). A panel
  // for an application nobody has offered an interview is not drawn. Cancelling emails the candidate and takes the event off
  // every calendar, so it is a destructive item in the panel header's menu, confirmed in a dialog (A2).
  let { applicationId, mayCancel }: { applicationId: string; mayCancel: boolean } = $props();

  const queryClient = useQueryClient();
  // InterviewChanged refetches it live (#lib/realtime.ts).
  const interviews = createListApplicationInterviews(() => applicationId);
  const cancel = createCancelInterview(() => ({
    mutation: { onSettled: () => invalidatePath(queryClient, `${applicationsPath}/${applicationId}`) },
  }));

  const list = $derived(interviews.data?.data);
  // Newest first: the open or booked one, else how the latest ended.
  const live = $derived(list?.find((interview) => interview.status === 'Open' || interview.status === 'Booked'));
  const shown = $derived(live ?? list?.[0]);

  let confirmOpen = $state(false);
  // Cancel was chosen in the menu: its dialog opens once the menu has closed and focus is back on the menu's button.
  let cancelChosen = false;

  const firstName = (name: string) => name.split(' ')[0] ?? name;
  // A window's last day: its end is the start of the day after.
  const lastDay = (interview: ApplicationInterview) => formatDateIn(new Date(Date.parse(interview.windowEnd) - 1).toISOString(), interview.timeZone);

  /** How a cancelled or expired interview ended, in one line. */
  function ended(interview: ApplicationInterview): string {
    if (interview.status === 'Expired') return `The candidate did not pick a time before ${lastDay(interview)}.`;
    const by = interview.cancelledByCandidate ? ' by the candidate' : '';
    return interview.startsAt ? `The interview on ${formatDateTime(interview.startsAt)} was cancelled${by}.` : `The invitation was cancelled${by}.`;
  }

  async function confirmCancel() {
    if (!live) return;
    await cancel.mutateAsync({ interviewId: live.id });
  }
</script>

{#snippet menu()}
  {#if live && mayCancel}
    <MoreMenu label="Interview actions" quiet size="icon" onclosed={() => cancelChosen && ((cancelChosen = false), (confirmOpen = true))}>
      <DropdownMenu.Item variant="destructive" onSelect={() => (cancelChosen = true)}><CalendarXIcon aria-hidden="true" />Cancel interview</DropdownMenu.Item>
    </MoreMenu>
  {/if}
{/snippet}

{#if interviews.isError}
  <Panel title="Interview"><QueryError message={describeError(interviews.error, 'Could not load the interview')} onretry={() => interviews.refetch()} inset /></Panel>
{:else if shown}
  <Panel title="Interview" actions={live && mayCancel ? menu : undefined}>
    {#if shown.status === 'Open'}
      <div class="flex flex-col gap-0.5">
        <p class="font-semibold">Waiting for the candidate to pick a time</p>
        <p class="text-muted-foreground">Times from {formatDateIn(shown.windowStart, shown.timeZone)} to {lastDay(shown)}.</p>
      </div>
    {:else if shown.status === 'Booked'}
      <div class="flex flex-col gap-4">
        <ul aria-label="Interviewers" class="flex flex-col gap-3">
          {#each shown.interviewers as person (person.userId)}
            <li class="flex min-w-0 flex-col gap-0.5">
              <span class="flex min-w-0 flex-wrap items-center gap-x-2 font-semibold">
                <span class="min-w-0 break-words">{person.displayName}</span>
                {#if person.isOrganizer}<Badge variant="muted">Organizer</Badge>{/if}
              </span>
              {#if person.syncStatus === 'Failed'}
                <FailureText role="status" message={person.syncProblem ?? `Could not put this on ${firstName(person.displayName)}'s calendar.`} />
              {:else if person.syncStatus === 'Pending'}
                <p class="text-muted-foreground">Not on {firstName(person.displayName)}'s calendar yet.</p>
              {/if}
            </li>
          {/each}
        </ul>
        {#if shown.meetingUrl}
          <a
            href={shown.meetingUrl}
            target="_blank"
            rel="noopener noreferrer"
            class="inline-flex min-h-6 w-fit items-center gap-1.5 font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11"
          >
            <VideoIcon aria-hidden="true" class="size-4" />Join the meeting<span class="sr-only"> (opens in a new tab)</span>
          </a>
        {/if}
      </div>
    {:else}
      <p class="text-muted-foreground">{ended(shown)}</p>
    {/if}
  </Panel>
{/if}

<ConfirmDialog
  bind:open={confirmOpen}
  title="Cancel this interview?"
  description={live?.status === 'Booked'
    ? 'The candidate is told, and it leaves every interviewer\'s calendar.'
    : "The candidate's link stops working."}
  cancelLabel="Keep it"
  confirmLabel="Cancel interview"
  failure="Could not cancel the interview"
  onconfirm={confirmCancel}
>
  {#snippet icon()}<CalendarXIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>
