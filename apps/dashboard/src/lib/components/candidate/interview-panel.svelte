<script lang="ts">
  import CalendarXIcon from '~icons/lucide/calendar-x';
  import { createCancelInterview, createListApplicationInterviews } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import InterviewDetails from '#lib/components/candidate/interview-details.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
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
    <InterviewDetails interview={shown} />
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
