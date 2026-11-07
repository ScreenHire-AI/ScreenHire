<script lang="ts">
  import CalendarClockIcon from '~icons/lucide/calendar-clock';
  import CalendarXIcon from '~icons/lucide/calendar-x';
  import { createCancelInterview, createListApplicationInterviews } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import InterviewDetails from '#lib/components/candidate/interview-details.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import OfferNewTimesSheet from '#lib/components/offer-new-times-sheet.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';

  // The candidate's interview (docs/architecture/interview-scheduling.md, design system ADR-0017): what is open or booked, or
  // how the last one ended. The time of a booked interview is the header's one fact and is not said again here (W7). A panel
  // for an application nobody has offered an interview is not drawn. Cancelling emails the candidate and takes the event off
  // every calendar, so it is a destructive item in the panel header's menu, confirmed in a dialog (A2). Offer new times is the
  // other item, for a booked interview: the candidate picks again from days the person chooses, and the booked time stays held
  // until they do. Both need the permission that emails candidates, since both email the candidate.
  let { applicationId, candidateName, mayChange, ondone }: { applicationId: string; candidateName: string; mayChange: boolean; ondone?: (message: string) => void } =
    $props();

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
  let offerOpen = $state(false);
  // An item was chosen in the menu: its dialog or sheet opens once the menu has closed and focus is back on the menu's button.
  let chosen: 'cancel' | 'offer' | null = null;

  function menuClosed() {
    if (chosen === 'cancel') confirmOpen = true;
    if (chosen === 'offer') offerOpen = true;
    chosen = null;
  }

  async function confirmCancel() {
    if (!live) return;
    await cancel.mutateAsync({ interviewId: live.id });
  }
</script>

{#snippet menu()}
  {#if live && mayChange}
    <MoreMenu label="Interview actions" quiet size="icon" onclosed={menuClosed}>
      {#if live.status === 'Booked'}
        <DropdownMenu.Item onSelect={() => (chosen = 'offer')}><CalendarClockIcon aria-hidden="true" />Offer new times</DropdownMenu.Item>
      {/if}
      <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = 'cancel')}><CalendarXIcon aria-hidden="true" />Cancel interview</DropdownMenu.Item>
    </MoreMenu>
  {/if}
{/snippet}

{#if interviews.isError}
  <Panel title="Interview"><QueryError message={describeError(interviews.error, 'Could not load the interview')} onretry={() => interviews.refetch()} inset /></Panel>
{:else if shown}
  <Panel title="Interview" actions={live && mayChange ? menu : undefined}>
    <InterviewDetails interview={shown} {candidateName} />
  </Panel>
{/if}

{#if live?.status === 'Booked' && mayChange}
  {#key live.id}
    <OfferNewTimesSheet bind:open={offerOpen} {applicationId} interview={live} {candidateName} {ondone} />
  {/key}
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
