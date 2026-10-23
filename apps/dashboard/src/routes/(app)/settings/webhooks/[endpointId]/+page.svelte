<script lang="ts">
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import KeyRoundIcon from '@lucide/svelte/icons/key-round';
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import PowerIcon from '@lucide/svelte/icons/power';
  import PowerOffIcon from '@lucide/svelte/icons/power-off';
  import RotateCcwIcon from '@lucide/svelte/icons/rotate-ccw';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import type { WebhookDeliveryEntry, WebhookEndpointEntry } from '@screenhire/api-client';
  import {
    ApiError,
    createDeleteWebhookEndpoint,
    createGetWebhookEndpoint,
    createListWebhookDeliveries,
    createReplayWebhookDelivery,
    createRotateWebhookSecret,
    createUpdateWebhookEndpoint,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import ActionBar from '#lib/components/action-bar.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SecretDialog from '#lib/components/secret-dialog.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { deliveryStatus, endpointStatus } from '#lib/webhooks.js';
  import type { PageProps } from './$types';

  // One webhook endpoint (workflow-engine.md section 8), on the detail template: whether it is on, its delivery log with
  // replay, its events and its signing secret. Turning it off or on is undone on this screen, so it needs no dialog;
  // rotating the secret breaks receivers until they take the new one, and deleting is for good, so both confirm (A2).
  let { params }: PageProps = $props();

  const webhooksPath = '/v1/webhooks';
  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const settled = () => invalidatePath(queryClient, webhooksPath);

  const endpoint = createGetWebhookEndpoint(() => params.endpointId);
  // The log refreshes while a delivery is still being tried, so retries show without a reload.
  const deliveries = createListWebhookDeliveries(
    () => params.endpointId,
    undefined,
    () => ({ query: { refetchInterval: (query) => (query.state.data?.data.some((delivery) => delivery.status === 'Pending') ? 5000 : false) } }),
  );
  const update = createUpdateWebhookEndpoint(() => ({ mutation: { onSettled: settled } }));
  const remove = createDeleteWebhookEndpoint(() => ({ mutation: { onSettled: settled } }));
  const rotate = createRotateWebhookSecret(() => ({ mutation: { onSettled: settled } }));
  const replay = createReplayWebhookDelivery(() => ({ mutation: { onSettled: settled } }));

  const detail = $derived(endpoint.data?.data);
  const notFound = $derived(endpoint.error instanceof ApiError && endpoint.error.status === 404);
  const log = $derived(deliveries.data?.data ?? []);
  const title = $derived(detail ? (detail.description ?? detail.url) : 'Endpoint');
  const editHref = $derived(`/settings/webhooks/${params.endpointId}/edit`);

  let actionFailure = $state('');
  let announcement = $state('');

  async function setEnabled(current: WebhookEndpointEntry, enabled: boolean) {
    actionFailure = '';
    try {
      await update.mutateAsync({
        endpointId: current.id,
        data: { url: current.url, events: current.events, description: current.description, enabled },
      });
      announcement = enabled ? 'The endpoint is on.' : 'The endpoint is off.';
    } catch (error) {
      actionFailure = describeError(error, enabled ? 'Could not turn the endpoint on' : 'Could not turn the endpoint off');
    }
  }

  let replayFailure = $state('');
  async function replayDelivery(delivery: WebhookDeliveryEntry) {
    replayFailure = '';
    try {
      await replay.mutateAsync({ endpointId: params.endpointId, deliveryId: delivery.id });
      announcement = `The ${delivery.eventType} delivery is being sent again.`;
    } catch (error) {
      replayFailure = describeError(error, 'Could not replay the delivery');
    }
  }

  // Rotate and delete confirm in a dialog that the More menu opens once it has closed.
  let rotateOpen = $state(false);
  let deleteOpen = $state(false);
  let chosen: 'rotate' | 'delete' | null = null;
  const onMenuClosed = () => {
    if (chosen === 'rotate') rotateOpen = true;
    else if (chosen === 'delete') deleteOpen = true;
    chosen = null;
  };

  let secret = $state('');
  let secretOpen = $state(false);
  async function rotateSecret() {
    const response = await rotate.mutateAsync({ endpointId: params.endpointId });
    secret = response.data.signingSecret;
    secretOpen = true;
  }

  async function deleteEndpoint() {
    await remove.mutateAsync({ endpointId: params.endpointId });
    await goto('/settings/webhooks');
  }

  const response = (delivery: WebhookDeliveryEntry) =>
    [delivery.lastStatusCode ? `HTTP ${delivery.lastStatusCode}` : null, delivery.lastError].filter(Boolean).join(', ');
</script>

<svelte:head>
  <title>{title} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Webhooks', href: '/settings/webhooks' }, { label: title }]} back />

<main class={['flex flex-col', sidebar.isMobile && detail && !detail.enabled && 'pb-28']}>
  {#if endpoint.isError && !detail}
    <HeaderBand title={notFound ? 'Endpoint not found' : 'Endpoint'} />
    <div class="mx-auto w-full max-w-page px-4 py-6 md:px-6">
      <Panel title={notFound ? 'Endpoint not found' : 'Could not load the endpoint'}>
        {#if notFound}
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>It was deleted, or it belongs to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content>
              <Button href="/settings/webhooks" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to webhooks</Button>
            </Empty.Content>
          </Empty.Root>
        {:else}
          <QueryError message={describeError(endpoint.error, 'Could not load the endpoint')} onretry={() => endpoint.refetch()} inset />
        {/if}
      </Panel>
    </div>
  {:else if !detail}
    <div aria-hidden="true" class="border-b border-border bg-card">
      <div class="mx-auto flex w-full max-w-page flex-col gap-4 px-4 py-5 md:px-6">
        <Skeleton class="h-9 w-full max-w-80" />
        <Skeleton class="h-10 w-full max-w-120" />
      </div>
    </div>
    <div aria-hidden="true" class="mx-auto flex w-full max-w-page flex-col gap-5 px-4 py-6 md:px-6 lg:flex-row">
      <Skeleton class="h-80 min-w-0 flex-1 rounded-panel" />
      <Skeleton class="h-48 rounded-panel lg:basis-side" />
    </div>
    <p class="sr-only" role="status">Loading the endpoint…</p>
  {:else}
    <HeaderBand {title}>
      {#snippet status()}<StatusBadge {...endpointStatus(detail)} />{/snippet}
      {#snippet actions()}
        <!-- Turning it on is the page's one primary action, while it is off; a phone has it in the action bar. -->
        {#if !detail.enabled && !sidebar.isMobile}{@render turnOnButton(detail)}{/if}
        {#if !sidebar.isMobile}
          <Button href={editHref} variant="outline" size="lg"><PencilIcon aria-hidden="true" />Edit</Button>
        {/if}
        <MoreMenu label="More actions" onclosed={onMenuClosed}>
          {#if sidebar.isMobile}
            <DropdownMenu.Item onSelect={() => goto(editHref)}><PencilIcon aria-hidden="true" />Edit</DropdownMenu.Item>
          {/if}
          {#if detail.enabled}
            <DropdownMenu.Item onSelect={() => setEnabled(detail, false)}><PowerOffIcon aria-hidden="true" />Turn off</DropdownMenu.Item>
          {/if}
          <DropdownMenu.Separator />
          <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = 'rotate')}><KeyRoundIcon aria-hidden="true" />Rotate signing secret</DropdownMenu.Item>
          <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = 'delete')}><Trash2Icon aria-hidden="true" />Delete endpoint</DropdownMenu.Item>
        </MoreMenu>
      {/snippet}
      {#snippet facts()}
        {#if detail.description}
          <Fact label="URL"><TruncatedText text={detail.url} /></Fact>
        {/if}
        <Fact label="Added"><RelativeTime at={detail.createdAt} /></Fact>
        <Fact label="Secret rotated"><RelativeTime at={detail.secretRotatedAt} /></Fact>
      {/snippet}
    </HeaderBand>

    <div class="mx-auto flex w-full max-w-page flex-col gap-5 px-4 py-6 md:px-6">
      <p role="status" class="sr-only">{announcement}</p>
      <FailureText message={actionFailure} />

      <!-- Why it is off comes first in the page's order, so one column starts with it. From lg it sits at the top of the
           side column, and the main column spans both of the side column's rows (section 7). -->
      <div class="flex flex-col gap-5 lg:grid lg:grid-cols-[minmax(0,1fr)_var(--container-side)] lg:grid-rows-[auto_1fr] lg:items-start">
        {#if detail.disabledReason}
          <div class="min-w-0 lg:col-start-2 lg:row-start-1">
            <Panel title="Why it is off">
              <p>{detail.disabledReason}</p>
              {#if detail.disabledAt}<p class="mt-2 text-muted-foreground">Turned off <RelativeTime at={detail.disabledAt} /></p>{/if}
            </Panel>
          </div>
        {/if}

        <div class="flex min-w-0 flex-col gap-5 lg:col-start-1 lg:row-span-2 lg:row-start-1">
          <Panel title="Deliveries" count={deliveries.isSuccess ? log.length : undefined} flush footer={replayFailure || !detail.enabled ? deliveriesFooter : undefined}>
            {#if deliveries.isError}
              <div class="p-4">
                <QueryError message={describeError(deliveries.error, 'Could not load the deliveries')} onretry={() => deliveries.refetch()} inset />
              </div>
            {:else if deliveries.isPending}
              <div aria-hidden="true" class="flex flex-col">
                {#each [0, 1, 2] as row (row)}
                  <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                    <Skeleton class="h-4 w-20" />
                    <Skeleton class="h-4 min-w-0 flex-1" />
                    <Skeleton class="h-4 w-24" />
                  </div>
                {/each}
              </div>
              <p class="sr-only" role="status">Loading the deliveries…</p>
            {:else if log.length === 0}
              <Empty.Root>
                <Empty.Header>
                  <Empty.Description>No deliveries yet. One is sent when a chosen event happens.</Empty.Description>
                </Empty.Header>
              </Empty.Root>
            {:else}
              <ul class="md:hidden">
                {#each log as delivery (delivery.id)}
                  <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
                    <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                      <span class="flex min-w-0 flex-wrap items-center gap-2">
                        {@render deliveryBadge(delivery)}
                        <span class="min-w-0 font-mono text-code break-all">{delivery.eventType}</span>
                      </span>
                      <span class="flex flex-wrap gap-x-3 text-caption text-muted-foreground">
                        <DateText at={delivery.createdAt} />
                        {#if response(delivery)}<span class="min-w-0 break-words">{response(delivery)}</span>{/if}
                      </span>
                    </div>
                    {@render replayButton(delivery)}
                  </li>
                {/each}
              </ul>
              <div class="overflow-x-auto max-md:hidden">
                <Table.Root>
                  <Table.Header>
                    <Table.Row class="bg-muted hover:bg-muted">
                      <Table.Head class="px-4.5">Status</Table.Head>
                      <Table.Head>Event</Table.Head>
                      <Table.Head class="text-right">Attempts</Table.Head>
                      <Table.Head>Response</Table.Head>
                      <Table.Head>Sent</Table.Head>
                      <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                    </Table.Row>
                  </Table.Header>
                  <Table.Body>
                    {#each log as delivery (delivery.id)}
                      <Table.Row class="h-12">
                        <Table.Cell class="px-4.5">{@render deliveryBadge(delivery)}</Table.Cell>
                        <Table.Cell class="font-mono text-code whitespace-nowrap">{delivery.eventType}</Table.Cell>
                        <Table.Cell class="text-right tabular-nums">{delivery.attempts}</Table.Cell>
                        <Table.Cell class="w-full max-w-0 text-muted-foreground">
                          {#if response(delivery)}<TruncatedText text={response(delivery)} />{/if}
                        </Table.Cell>
                        <Table.Cell class="text-muted-foreground"><DateText at={delivery.createdAt} /></Table.Cell>
                        <Table.Cell class="pr-4.5 text-right">{@render replayButton(delivery)}</Table.Cell>
                      </Table.Row>
                    {/each}
                  </Table.Body>
                </Table.Root>
              </div>
            {/if}
          </Panel>
        </div>

        <div class="flex min-w-0 flex-col gap-5 lg:col-start-2 lg:row-start-2">
          <Panel title="Events" count={detail.events.length}>
            <ul class="flex flex-col gap-2" aria-label="Event types">
              {#each detail.events as type (type)}
                <li class="min-w-0 font-mono text-code break-all">{type}</li>
              {/each}
            </ul>
          </Panel>
        </div>
      </div>
    </div>

    {#if sidebar.isMobile && !detail.enabled}
      <ActionBar>{@render turnOnButton(detail)}</ActionBar>
    {/if}
  {/if}
</main>

{#snippet turnOnButton(current: WebhookEndpointEntry)}
  <Button size="lg" disabled={update.isPending} onclick={() => setEnabled(current, true)}><PowerIcon aria-hidden="true" />Turn on</Button>
{/snippet}

<!-- Calm deliveries are quiet in the log; one that is still being tried or gave up is filled (T3). -->
{#snippet deliveryBadge(delivery: WebhookDeliveryEntry)}
  {@const status = deliveryStatus[delivery.status]}
  <StatusBadge status={status.status} label={status.label} quiet={status.status === 'clear' || status.status === 'neutral'} />
{/snippet}

<!-- The API refuses a replay while the event is still being tried, or while the endpoint is off. -->
{#snippet replayButton(delivery: WebhookDeliveryEntry)}
  {#if detail?.enabled && delivery.status !== 'Pending'}
    <IconButton label={`Replay the ${delivery.eventType} delivery`} disabled={replay.isPending} onclick={() => replayDelivery(delivery)}>
      <RotateCcwIcon aria-hidden="true" />
    </IconButton>
  {/if}
{/snippet}

{#snippet deliveriesFooter()}
  {#if replayFailure}
    <FailureText message={replayFailure} />
  {:else}
    <p class="text-muted-foreground">Turn the endpoint on to replay deliveries.</p>
  {/if}
{/snippet}

<ConfirmDialog
  bind:open={rotateOpen}
  title="Rotate the signing secret?"
  description="The old secret stops working at once. Receivers fail to check deliveries until they have the new one."
  cancelLabel="Keep it"
  confirmLabel="Rotate secret"
  failure="Could not rotate the secret"
  onconfirm={rotateSecret}
>
  {#snippet icon()}<KeyRoundIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<ConfirmDialog
  bind:open={deleteOpen}
  title="Delete this endpoint?"
  description="Deliveries stop and its log is deleted. This cannot be undone."
  cancelLabel="Keep it"
  confirmLabel="Delete endpoint"
  failure="Could not delete the endpoint"
  onconfirm={deleteEndpoint}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<SecretDialog bind:open={secretOpen} title="Your new signing secret" label="Signing secret" value={secret} onclose={() => (secret = '')} />
