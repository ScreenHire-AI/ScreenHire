<script lang="ts">
  import CalendarClockIcon from '~icons/lucide/calendar-clock';
  import CalendarCogIcon from '~icons/lucide/calendar-cog';
  import CalendarXIcon from '~icons/lucide/calendar-x';
  import MessageSquareIcon from '~icons/lucide/message-square';
  import { ApiError, createAskForMoreInterviewTimes, createBookInterviewTime, createCancelInterview, createListApplicationInterviews } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import ChangeOptionSheet from '#lib/components/change-option-sheet.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import InterviewDetails from '#lib/components/candidate/interview-details.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import WhoIsFree from '#lib/components/who-is-free.svelte';
  import { linkedTo } from '#lib/linked.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // The candidate's interview (docs/architecture/interview-scheduling.md, design system ADR-0017): what is open or booked, or
  // how the last one ended. A booked interview's time is the panel's first fact. A panel for an application nobody has offered
  // an interview is not drawn. When the next step is the team's, the panel holds it: the times the candidate sent, each with
  // who is free and its Book button, and Ask for more times. The menu changes how they pick (an open interview) or moves a
  // booked one, both with the same three ways, and cancels, a destructive item confirmed in a dialog (A2). Each needs the
  // permission that emails candidates, since each emails the candidate.
  let {
    applicationId,
    jobId,
    candidateName,
    mayChange,
    ondone,
  }: { applicationId: string; jobId: string; candidateName: string; mayChange: boolean; ondone?: (message: string) => void } = $props();

  const queryClient = useQueryClient();
  const refresh = { mutation: { onSettled: () => invalidatePath(queryClient, `${applicationsPath}/${applicationId}`) } };
  // InterviewChanged refetches it live (#lib/realtime.ts).
  const interviews = createListApplicationInterviews(() => applicationId);
  const cancel = createCancelInterview(() => refresh);
  const book = createBookInterviewTime(() => refresh);
  const moreTimes = createAskForMoreInterviewTimes(() => refresh);

  const list = $derived(interviews.data?.data);
  // Newest first: the open or booked one, else how the latest ended.
  const live = $derived(list?.find((interview) => interview.status === 'Open' || interview.status === 'Booked'));
  const shown = $derived(live ?? list?.[0]);
  const candidateTimes = $derived(live?.waitsForTeam && live.option === 'TheirTimes' ? (live.candidateTimes ?? []) : []);
  const nameOf = (userId: string) => live?.interviewers.find((person) => person.userId === userId)?.displayName ?? 'A teammate';
  const firstName = $derived(candidateName.split(' ')[0] || candidateName);

  let confirmOpen = $state(false);
  let changeOpen = $state(false);
  let askOpen = $state(false);
  let note = $state('');
  let failure = $state('');
  let booking = $state<string | null>(null);
  // An item was chosen in the menu: its dialog or sheet opens once the menu has closed and focus is back on the menu's button.
  let chosen: 'cancel' | 'change' | null = null;

  function menuClosed() {
    if (chosen === 'cancel') confirmOpen = true;
    if (chosen === 'change') changeOpen = true;
    chosen = null;
  }

  async function confirmCancel() {
    if (!live) return;
    await cancel.mutateAsync({ interviewId: live.id });
  }

  async function bookTime(start: string) {
    if (!live) return;
    failure = '';
    booking = start;
    try {
      await book.mutateAsync({ interviewId: live.id, data: { start } });
      ondone?.(`Booked the time with ${candidateName}.`);
    } catch (error) {
      failure = error instanceof ApiError && error.status === 409 ? describeError(error, 'That time could not be booked') : describeError(error, 'Could not book the time');
    } finally {
      booking = null;
    }
  }

  async function askForMore(event: SubmitEvent) {
    event.preventDefault();
    if (!live) return;
    failure = '';
    try {
      await moreTimes.mutateAsync({ interviewId: live.id, data: { note: note.trim() || null } });
      askOpen = false;
      note = '';
      ondone?.(`Asked ${candidateName} for more times.`);
    } catch (error) {
      failure = describeError(error, 'Could not ask for more times');
    }
  }
</script>

{#snippet menu()}
  {#if live && mayChange}
    <MoreMenu label="Interview actions" quiet size="icon" onclosed={menuClosed}>
      {#if live.status === 'Booked'}
        <DropdownMenu.Item onSelect={() => (chosen = 'change')}><CalendarClockIcon aria-hidden="true" />Move the interview</DropdownMenu.Item>
      {:else}
        <DropdownMenu.Item onSelect={() => (chosen = 'change')}><CalendarCogIcon aria-hidden="true" />Change how they pick</DropdownMenu.Item>
      {/if}
      <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = 'cancel')}><CalendarXIcon aria-hidden="true" />Cancel interview</DropdownMenu.Item>
    </MoreMenu>
  {/if}
{/snippet}

{#if interviews.isError}
  <Panel title="Interview"><QueryError message={describeError(interviews.error, 'Could not load the interview')} onretry={() => interviews.refetch()} inset /></Panel>
{:else if shown}
  <!-- Activity's and the toasts' interview links land here (#interview). -->
  <Panel id="interview" title="Interview" actions={live && mayChange ? menu : undefined} {@attach linkedTo('interview')}>
    <div class="flex flex-col gap-4">
      <InterviewDetails interview={shown} {candidateName} />
      {#if live && candidateTimes.length > 0}
        <WhoIsFree
          {jobId}
          interviewerUserIds={live.interviewers.map((person) => person.userId)}
          starts={candidateTimes}
          durationMinutes={live.durationMinutes}
          interviewId={live.id}
          {nameOf}
          label={`Times ${firstName} is free`}
        >
          {#snippet row(start)}
            {#if mayChange}
              <Button type="button" variant="outline" disabled={booking !== null} onclick={() => bookTime(start)}>Book</Button>
            {/if}
          {/snippet}
        </WhoIsFree>
        {#if mayChange}
          <Button type="button" variant="outline" class="w-fit" onclick={() => (askOpen = true)}><MessageSquareIcon aria-hidden="true" />Ask for more times</Button>
        {/if}
      {/if}
      <FailureText message={failure} />
    </div>
  </Panel>
{/if}

{#if live && mayChange}
  {#key live.id}
    <ChangeOptionSheet bind:open={changeOpen} {applicationId} {jobId} interview={live} {candidateName} {ondone} />
  {/key}
{/if}

<Sheet.Root bind:open={askOpen}>
  <Sheet.Content side="right" class="overflow-y-auto data-[side=right]:w-full data-[side=right]:sm:max-w-xl">
    <Sheet.Header>
      <Sheet.Title>Ask for more times</Sheet.Title>
      <Sheet.Description>{firstName} is emailed on the same link to send a few more times.</Sheet.Description>
    </Sheet.Header>
    <form class="flex flex-col gap-5 px-4 pb-4" onsubmit={askForMore}>
      <Field.Field>
        <Field.Label for="more-times-note">Message (optional)</Field.Label>
        <Textarea id="more-times-note" bind:value={note} maxlength={500} rows={3} placeholder="Could you do an afternoon next week?" />
      </Field.Field>
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={moreTimes.isPending}>Ask for more times</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>

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
