<script lang="ts">
  import BanIcon from '~icons/lucide/ban';
  import InboxIcon from '~icons/lucide/inbox';
  import SaveIcon from '~icons/lucide/save';
  import SendIcon from '~icons/lucide/send';
  import Trash2Icon from '~icons/lucide/trash-2';
  import type { DataRequestEntry, HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createDeclineDataRequest,
    createEraseDataRequest,
    createGetOrganizationRetention,
    createListDataRequests,
    createPreviewOrganizationRetention,
    createSendDataRequestCopy,
    createUpdateOrganizationRetention,
    getGetOrganizationRetentionHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import {
    createSortedRowModel,
    createTable,
    rowSortingFeature,
    sortFn_alphanumeric,
    sortFn_basic,
    tableFeatures,
    type ColumnDef,
  } from '@tanstack/svelte-table';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { formatDate } from '#lib/dates.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, candidatesPath, dataRequestsPath, jobsPath, retentionPath, talentPoolPath } from '#lib/realtime.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { cn } from '#lib/utils.js';

  // The Data requests page (docs/architecture/data-requests.md): what candidates asked for through the careers site's
  // privacy page, and the record retention setting. The list template (design system section 7) with one more panel.
  // The team answers a request here: Send a copy emails the candidate a link to a file of their data, Erase deletes the
  // candidate (the name is typed to confirm), Decline emails them the team's reason. Each request has a due date, the
  // team's own target. The settings layout has already checked Erase candidates and every job, as the API does.
  // The retention setting is the one thing on this page people edit, so its Last updated and History sit in its panel's
  // header (T6), not in the page's header band, which would read as the requests' own.
  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();

  const requests = createListDataRequests();
  const list = $derived(requests.data?.data.items ?? []);
  const openCount = $derived(requests.data?.data.openCount);

  const refreshRequests = () => invalidatePath(queryClient, dataRequestsPath);
  const sendCopy = createSendDataRequestCopy(() => ({ mutation: { onSettled: refreshRequests } }));
  // Erasing also empties the candidate from every list and changes what retention would remove.
  const erase = createEraseDataRequest(() => ({
    mutation: {
      onSettled: () => Promise.all([dataRequestsPath, retentionPath, candidatesPath, applicationsPath, jobsPath, talentPoolPath].map((path) => invalidatePath(queryClient, path))),
    },
  }));
  const decline = createDeclineDataRequest(() => ({ mutation: { onSettled: refreshRequests } }));

  // Two people may act on one request. The API answers the second with a 409, which reads as this sentence, not as a
  // failed request, and the list refetches (onSettled) to show how it was handled.
  const alreadyHandled = 'This request was already handled.';
  async function answer<T>(action: () => Promise<T>): Promise<T> {
    try {
      return await action();
    } catch (error) {
      throw error instanceof ApiError && error.status === 409 ? new ApiError(409, { detail: alreadyHandled }) : error;
    }
  }

  const kindLabel = (request: DataRequestEntry) => (request.kind === 'Copy' ? 'Copy of data' : 'Erasure');
  // A copy is built in the background after Send a copy: until the list says Completed, the row says so.
  let preparing = $state<string[]>([]);
  const statusLabel = (request: DataRequestEntry) =>
    request.status === 'Completed' ? 'Completed' : request.status === 'Declined' ? 'Declined' : preparing.includes(request.id) ? 'Preparing copy' : request.overdue ? 'Overdue' : 'Open';
  // The row's second line says how a finished request ended (L1: one line).
  const outcome = (request: DataRequestEntry): string => {
    if (request.status === 'Declined') return request.declineReason ? `Declined: ${request.declineReason}` : 'Declined';
    if (request.status !== 'Completed') return '';
    if (request.kind === 'Copy') return request.copySentAt ? `Copy sent ${formatDate(request.copySentAt)}` : '';
    return request.resolvedAt ? `Erased ${formatDate(request.resolvedAt)}` : 'Erased';
  };

  // The table sorts (L2) and opens on what needs the team: open requests by due date, the earliest first, then the answered ones,
  // the latest answered first. An answered request has no due date worth sorting by, so its key ranks after every open one.
  const dueKey = (request: DataRequestEntry) =>
    request.status === 'Open' ? request.dueAt : `~${String(9e15 - Date.parse(request.resolvedAt ?? request.receivedAt)).padStart(16, '0')}`;

  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, DataRequestEntry, any>[] = [
    { id: 'status', header: 'Status', accessorFn: (request) => statusLabel(request), sortFn: 'alphanumeric' },
    { id: 'candidate', header: 'Candidate', accessorFn: (request) => request.candidate?.name ?? '', sortFn: 'alphanumeric' },
    { id: 'request', header: 'Request', accessorFn: (request) => kindLabel(request), sortFn: 'alphanumeric' },
    { id: 'received', header: 'Received', accessorFn: (request) => request.receivedAt, sortFn: 'basic', sortDescFirst: true },
    { id: 'due', header: 'Due', accessorFn: (request) => dueKey(request), sortFn: 'basic' },
    { id: 'outcome', header: 'Outcome', accessorFn: (request) => outcome(request), sortFn: 'alphanumeric' },
  ];
  const sort = createTableSort('data-requests', { id: 'due', desc: false }, ['status', 'candidate', 'request', 'received', 'due', 'outcome']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return list;
    },
    getRowId: (request) => request.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));

  let announcement = $state('');

  // The row's menu opens a dialog once the menu has closed and given focus back to its button (the api-keys pattern).
  let acting = $state<DataRequestEntry | null>(null);
  let chosen: 'copy' | 'erase' | 'decline' | null = null;
  let copyOpen = $state(false);
  let eraseOpen = $state(false);
  let declineOpen = $state(false);
  const onMenuClosed = () => {
    if (chosen === 'copy') copyOpen = true;
    if (chosen === 'erase') eraseOpen = true;
    if (chosen === 'decline') declineOpen = true;
    chosen = null;
  };
  const choose = (request: DataRequestEntry, action: NonNullable<typeof chosen>) => {
    acting = request;
    chosen = action;
  };
  const actingName = $derived(acting?.candidate?.name ?? 'this candidate');
  // Send a copy is for an open Copy request, Erase for an open Erasure request with a candidate left to erase, and Decline for any open one.
  const mayDecline = (request: DataRequestEntry) => request.status === 'Open';
  const maySend = (request: DataRequestEntry) => request.status === 'Open' && request.kind === 'Copy' && !preparing.includes(request.id);
  const mayErase = (request: DataRequestEntry) => request.status === 'Open' && request.kind === 'Erasure' && request.candidate !== null;

  async function sendTheCopy() {
    const request = acting;
    if (!request) return;
    await answer(() => sendCopy.mutateAsync({ id: request.id }));
    preparing = [...preparing, request.id];
    announcement = `A copy of ${request.candidate?.name ?? 'the candidate'}'s data is being prepared. They are emailed a link when it is ready.`;
  }

  async function eraseTheCandidate() {
    const request = acting;
    if (!request?.candidate) return;
    const name = request.candidate.name;
    await answer(() => erase.mutateAsync({ id: request.id, data: { confirmation: name } }));
    announcement = `${name} was erased.`;
  }

  const declineSchema = z.object({
    reason: z.string().trim().min(1, 'Say why you are declining.').max(500, 'Keep the reason to 500 characters or fewer.'),
  });
  const declineForm = superForm(defaults({ reason: '' }, zod4(declineSchema)), {
    SPA: true,
    validators: zod4(declineSchema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      const request = acting;
      if (!submitted.valid || !request) return;
      try {
        await answer(() => decline.mutateAsync({ id: request.id, data: { reason: submitted.data.reason.trim() } }));
        declineOpen = false;
        announcement = `The request from ${request.candidate?.name ?? 'the candidate'} was declined. They were emailed your reason.`;
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not decline the request')];
        }
      }
    },
  });
  const { form: declineValues, errors: declineErrors, enhance: declineEnhance, submitting: declining, reset: resetDecline } = declineForm;
  $effect(() => {
    if (declineOpen) resetDecline({ data: { reason: '' } });
  });

  // Record retention. Off by default; on, it deletes an ended application once it has been ended for the months set, daily.
  const retention = createGetOrganizationRetention();
  const retentionData = $derived(retention.data?.data);
  const savedMonths = $derived(retentionData?.months ?? null);
  const update = createUpdateOrganizationRetention(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, retentionPath) } }));

  const monthsMessage = 'Enter a whole number of months from 1 to 120.';
  const retentionSchema = z
    .object({ enabled: z.boolean(), months: z.number().nullable() })
    .superRefine((value, context) => {
      if (value.enabled && !(value.months !== null && Number.isInteger(value.months) && value.months >= 1 && value.months <= 120)) {
        context.addIssue({ code: 'custom', path: ['months'], message: monthsMessage });
      }
    });

  let saved = $state('');
  let confirmOpen = $state(false);
  let pendingMonths = $state<number | null>(null);
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ enabled: false, months: null as number | null }, zod4(retentionSchema)), {
    SPA: true,
    validators: zod4(retentionSchema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid || !retentionData) return;
      const months = submitted.data.enabled ? submitted.data.months : null;
      // Turning it on, or shortening it, deletes records now. Say how many and ask first; turning it off deletes nothing.
      const deletesNow = months !== null && (savedMonths === null || months < savedMonths);
      if (deletesNow && !(counts && counts.applications === 0)) {
        pendingMonths = months;
        confirmOpen = true;
        return;
      }
      try {
        await saveRetention(months);
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the setting')];
        }
      }
    },
  });

  async function saveRetention(months: number | null) {
    await update.mutateAsync({ data: { months } });
    saved = months === null ? 'Saved. Ended applications are kept.' : 'Saved.';
  }

  // The form starts from the saved setting once it loads; a refetch after that keeps what was typed.
  let started = false;
  $effect(() => {
    if (retentionData && !started) {
      started = true;
      reset({ data: { enabled: retentionData.months !== null, months: retentionData.months } });
    }
  });

  const validMonths = $derived(
    $form.enabled && typeof $form.months === 'number' && Number.isInteger($form.months) && $form.months >= 1 && $form.months <= 120 ? $form.months : undefined,
  );
  // The dry run follows the typed months once typing pauses, so it asks the API once, not for every digit.
  let previewMonths = $state<number | undefined>(undefined);
  $effect(() => {
    const wanted = validMonths;
    if (wanted === undefined) {
      previewMonths = undefined;
      return;
    }
    const timer = setTimeout(() => (previewMonths = wanted), 300);
    return () => clearTimeout(timer);
  });
  const preview = createPreviewOrganizationRetention(
    () => ({ months: previewMonths }),
    () => ({ query: { enabled: previewMonths !== undefined } }),
  );
  const checking = $derived(validMonths !== undefined && (previewMonths !== validMonths || preview.isFetching));
  // The count for the months in the field, never one left from other months.
  const counts = $derived(validMonths !== undefined && previewMonths === validMonths && !preview.isFetching && preview.isSuccess ? preview.data.data : undefined);

  const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`;
  const removal = (found: { applications: number; candidates: number }) =>
    `${plural(found.applications, 'application', 'applications')} from ${plural(found.candidates, 'candidate', 'candidates')}`;
  const previewSentence = (found: { applications: number; candidates: number }) => {
    if (found.applications === 0) return 'Nothing would be removed now.';
    return `${validMonths === savedMonths ? 'The next run removes' : 'Saving this removes'} ${removal(found)}${validMonths === savedMonths ? '' : ' now'}. Saved candidates are never removed.`;
  };

  async function confirmRetention() {
    await saveRetention(pendingMonths);
  }
</script>

<svelte:head>
  <title>Data requests · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Data requests' }]} />

<main class={[sidebar.isMobile && 'pb-6']}>
  <HeaderBand title="Data requests" count={openCount}>
    {#snippet status()}{#if openCount !== undefined}<span class="text-muted-foreground">open</span>{/if}{/snippet}
  </HeaderBand>

  <SettingsMenu>
    <p role="status" class="sr-only">{announcement}</p>
    <div class="flex min-w-0 flex-col gap-5">
      {#if requests.isError}
        <Panel title="Requests"><QueryError message={describeError(requests.error, 'Could not load the data requests')} onretry={() => requests.refetch()} inset /></Panel>
      {:else}
        <Panel title="Requests" flush>
          {#if requests.isPending}
            <div aria-hidden="true" class="flex flex-col">
              {#each [0, 1] as row (row)}
                <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                  <Skeleton class="h-4 w-20" />
                  <Skeleton class="h-4 min-w-0 flex-1" />
                  <Skeleton class="h-4 w-24" />
                </div>
              {/each}
            </div>
            <p class="sr-only" role="status">Loading the data requests…</p>
          {:else if list.length === 0}
            <Empty.Root>
              <Empty.Header>
                <Empty.Media variant="icon"><InboxIcon /></Empty.Media>
                <Empty.Title>No data requests yet</Empty.Title>
                <Empty.Description>When a candidate asks for a copy of their data or to be erased, the request appears here with a due date.</Empty.Description>
              </Empty.Header>
            </Empty.Root>
          {:else}
            <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
            <ul class="md:hidden">
              {#each sorted as request (request.id)}
                <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
                  <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                    <span class="flex min-w-0 flex-wrap items-center gap-2">
                      {@render status(request)}
                      {@render candidate(request)}
                    </span>
                    {#if request.candidate}<p class="text-caption break-words text-muted-foreground">{request.candidate.email}</p>{/if}
                    <span class="flex flex-wrap gap-1.5">
                      <Badge variant="muted">{kindLabel(request)}</Badge>
                      <Badge variant="muted">Received <DateText at={request.receivedAt} /></Badge>
                      {#if request.status === 'Open'}<Badge variant="muted">Due <DateText at={request.dueAt} /></Badge>{/if}
                    </span>
                    {#if outcome(request)}<p class="text-caption break-words text-muted-foreground">{outcome(request)}</p>{/if}
                  </div>
                  {@render requestMenu(request)}
                </li>
              {/each}
            </ul>
            <div class="overflow-x-auto max-md:hidden">
              <Table.Root>
                <Table.Header>
                  {#each table.getHeaderGroups() as group (group.id)}
                    <Table.Row class="bg-muted hover:bg-muted">
                      {#each group.headers as header (header.id)}
                        <SortHeader {header} class={header.id === 'status' ? 'px-4.5' : undefined} />
                      {/each}
                      <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                    </Table.Row>
                  {/each}
                </Table.Header>
                <Table.Body>
                  {#each sorted as request (request.id)}
                    <Table.Row class="h-12">
                      <Table.Cell class="px-4.5">{@render status(request)}</Table.Cell>
                      <Table.Cell class="w-2/3 max-w-0">{@render candidate(request)}</Table.Cell>
                      <Table.Cell class="whitespace-nowrap">{kindLabel(request)}</Table.Cell>
                      <Table.Cell class="text-muted-foreground"><DateText at={request.receivedAt} /></Table.Cell>
                      <Table.Cell class="text-muted-foreground">
                        {#if request.status === 'Open'}<DateText at={request.dueAt} />{:else}<span class="sr-only">No due date</span>{/if}
                      </Table.Cell>
                      <Table.Cell class="w-1/3 max-w-0 text-muted-foreground">{#if outcome(request)}<TruncatedText text={outcome(request)} />{/if}</Table.Cell>
                      <Table.Cell class="pr-4.5 text-right">{@render requestMenu(request)}</Table.Cell>
                    </Table.Row>
                  {/each}
                </Table.Body>
              </Table.Root>
            </div>
          {/if}
        </Panel>
      {/if}

      {#if retention.isError && !retentionData}
        <Panel title="Record retention"><QueryError message={describeError(retention.error, 'Could not load the retention setting')} onretry={() => retention.refetch()} inset /></Panel>
      {:else if !retentionData}
        <Panel title="Record retention">
          <div aria-hidden="true" class="flex flex-col gap-3"><Skeleton class="h-5 w-72 max-w-full" /><Skeleton class="h-4 w-full max-w-form" /></div>
          <p class="sr-only" role="status">Loading the retention setting…</p>
        </Panel>
      {:else}
        <form method="POST" use:enhance novalidate aria-label="Record retention">
          <Panel title="Record retention">
            {#snippet actions()}
              <HistoryFact value={retentionData.lastUpdated} name="Record retention" page={(before) => getGetOrganizationRetentionHistoryQueryOptions({ before })} />
            {/snippet}
            <div class="flex max-w-form flex-col gap-5">
              <div class="flex items-start gap-4">
                <div class="flex min-w-0 flex-1 flex-col gap-1">
                  <Field.Label for="retention-enabled">Delete ended applications</Field.Label>
                  <Field.Description id="retention-enabled-hint">Ended means hired, rejected or withdrawn, or a closed job, counted from when it ended.</Field.Description>
                </div>
                <Switch id="retention-enabled" bind:checked={$form.enabled} aria-describedby="retention-enabled-hint" class="mt-1" />
              </div>
              {#if $form.enabled}
                <Field.Field data-invalid={$errors.months ? true : undefined}>
                  <Field.Label for="retention-months">Keep for (months)</Field.Label>
                  <Input
                    id="retention-months"
                    name="months"
                    type="number"
                    inputmode="numeric"
                    min={1}
                    max={120}
                    step={1}
                    autocomplete="off"
                    class="w-32"
                    bind:value={$form.months}
                    aria-invalid={$errors.months ? 'true' : undefined}
                    aria-describedby={$errors.months ? 'retention-months-error retention-preview' : 'retention-preview'}
                  />
                  <Field.Error id="retention-months-error" errors={$errors.months?.map((message) => ({ message }))} />
                </Field.Field>
                <div id="retention-preview" role="status" class="empty:hidden">
                  {#if checking}
                    <p class="text-muted-foreground">Checking what this would remove…</p>
                  {:else if counts}
                    <p>{previewSentence(counts)}</p>
                  {:else if validMonths !== undefined && preview.isError}
                    <FailureText message={describeError(preview.error, 'Could not count what this would remove')} role="status" />
                  {/if}
                </div>
              {/if}
              <p class="text-muted-foreground">
                {#if retentionData.lastRun}Last run <RelativeTime at={retentionData.lastRun.at} />: removed {plural(retentionData.lastRun.applicationsRemoved, 'application', 'applications')}.{:else}Has not run yet.{/if}
              </p>
            </div>
            {#snippet footer()}
              <div class="flex flex-wrap items-center justify-end gap-3">
                <FailureText message={$errors._errors?.[0]} class="mr-auto" />
                <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
                <Button type="submit" size="lg" disabled={$submitting || checking}><SaveIcon aria-hidden="true" />Save</Button>
              </div>
            {/snippet}
          </Panel>
        </form>
      {/if}
    </div>
  </SettingsMenu>
</main>

{#snippet status(request: DataRequestEntry)}
  {#if request.status === 'Completed'}
    <StatusBadge status="done" label="Completed" quiet />
  {:else if request.status === 'Declined'}
    <StatusBadge status="notInEffect" label="Declined" quiet />
  {:else if preparing.includes(request.id)}
    <StatusBadge status="working" label="Preparing copy" quiet />
  {:else if request.overdue}
    <OverdueBadge dueAt={request.dueAt} />
  {:else}
    <!-- Within its due date an open request is the team's own work, not a state to flag: the word is for screen readers (L1, 3.2). -->
    <span class="sr-only">Open</span>
  {/if}
{/snippet}

{#snippet candidate(request: DataRequestEntry)}
  {#if request.candidate}
    <TruncatedText text={request.candidate.name} href={`/candidates/${request.candidate.id}`} class="font-semibold" />
  {:else}
    <span class="text-muted-foreground">Erased candidate</span>
  {/if}
{/snippet}

{#snippet requestMenu(request: DataRequestEntry)}
  {#if mayDecline(request)}
    <MoreMenu label={`Actions for ${request.candidate?.name ?? 'erased candidate'}, ${kindLabel(request).toLowerCase()}`} size="icon" quiet onclosed={onMenuClosed}>
      {#if maySend(request)}
        <DropdownMenu.Item onSelect={() => choose(request, 'copy')}><SendIcon aria-hidden="true" />Send a copy</DropdownMenu.Item>
      {/if}
      {#if mayErase(request)}
        <DropdownMenu.Item variant="destructive" onSelect={() => choose(request, 'erase')}><Trash2Icon aria-hidden="true" />Erase candidate</DropdownMenu.Item>
      {/if}
      <DropdownMenu.Item onSelect={() => choose(request, 'decline')}><BanIcon aria-hidden="true" />Decline request</DropdownMenu.Item>
    </MoreMenu>
  {/if}
{/snippet}

<ConfirmDialog
  bind:open={copyOpen}
  title={`Send ${actingName} a copy of their data?`}
  description="ScreenHire builds one file of what you hold on them and emails them a link to download it. The link expires, and the request is closed once the copy is sent."
  confirmLabel="Send copy"
  destructive={false}
  failure="Could not send the copy"
  onconfirm={sendTheCopy}
>
  {#snippet icon()}<SendIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<!-- The candidate asked for this, but erasing is for good, so the name is typed to be sure (design system A2).
     EraseCandidateDialog calls the candidate route, which would leave the request open; this one calls the request's. -->
<ConfirmDialog
  bind:open={eraseOpen}
  title="Erase this candidate?"
  description={`This erases ${actingName} and all of their applications, files, answers, notes and verification data, for good. ScreenHire keeps only a line in the team log that someone was erased, with no details. They are not emailed, because their address is erased too.`}
  confirmLabel="Erase candidate"
  failure="Could not erase the candidate"
  confirmText={acting?.candidate?.name}
  onconfirm={eraseTheCandidate}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<ConfirmDialog
  bind:open={confirmOpen}
  title={`Delete ended applications after ${pendingMonths ?? ''} months?`}
  description={counts && counts.applications > 0
    ? `Saving removes ${removal(counts)} now, for good, and each day after as more applications end. Saved candidates are never removed.`
    : 'Saving removes every application that ended longer ago than this, for good, and each day after as more applications end. Saved candidates are never removed.'}
  cancelLabel="Keep them"
  confirmLabel="Save and delete"
  failure="Could not save the setting"
  onconfirm={confirmRetention}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<Dialog.Root bind:open={declineOpen}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Decline this request?</Dialog.Title>
      <Dialog.Description>{actingName} is emailed your reason. Say plainly why you cannot act on the request.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:declineEnhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$declineErrors.reason ? true : undefined}>
        <Field.Label for="decline-reason">Reason</Field.Label>
        <Textarea
          id="decline-reason"
          name="reason"
          rows={4}
          maxlength={500}
          required
          bind:value={$declineValues.reason}
          aria-invalid={$declineErrors.reason ? 'true' : undefined}
          aria-describedby={$declineErrors.reason ? 'decline-reason-error' : undefined}
        />
        <Field.Error id="decline-reason-error" errors={$declineErrors.reason?.map((message) => ({ message }))} />
      </Field.Field>
      <FailureText message={$declineErrors._errors?.[0]} />
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$declining}><BanIcon aria-hidden="true" />Decline request</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
