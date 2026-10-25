<script lang="ts">
  import ExternalLinkIcon from '@lucide/svelte/icons/external-link';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import WebhookIcon from '@lucide/svelte/icons/webhook';
  import type { WebhookEndpointEntry } from '@screenhire/api-client';
  import { createGetMeta, createListWebhookEndpoints, createListWebhookEventTypes } from '@screenhire/api-client/svelte-query';
  import {
    createSortedRowModel,
    createTable,
    rowSortingFeature,
    sortFn_alphanumeric,
    sortFn_basic,
    tableFeatures,
    type ColumnDef,
  } from '@tanstack/svelte-table';
  import ActionBar from '#lib/components/action-bar.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { describeError } from '#lib/query.js';
  import { needsAttention } from '#lib/status.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { endpointStatus, eventLabel } from '#lib/webhooks.js';

  // The organization's webhook endpoints (workflow-engine.md section 8), on the list template. Each row opens the
  // endpoint: its events, delivery log and replay. Under the endpoints, a panel says what webhooks are for, how to check
  // a message and where the reference is (design system section 22): the events come from the API, and the reference's
  // origin from /v1/meta (ScreenHire:Domains:ApiHost).
  const sidebar = Sidebar.useSidebar();
  const endpoints = createListWebhookEndpoints();
  const eventTypes = createListWebhookEventTypes();
  const meta = createGetMeta();
  const apiOrigin = $derived(meta.data?.data.apiOrigin);
  const list = $derived(endpoints.data?.data ?? []);
  const name = (endpoint: WebhookEndpointEntry) => endpoint.description ?? endpoint.url;

  // The table sorts (L2), endpoints by name first.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, WebhookEndpointEntry, any>[] = [
    { id: 'status', header: 'Status', accessorFn: (endpoint) => endpointStatus(endpoint).label, sortFn: 'alphanumeric' },
    { id: 'name', header: 'Name', accessorFn: name, sortFn: 'alphanumeric' },
    { id: 'url', header: 'URL', accessorFn: (endpoint) => endpoint.url, sortFn: 'alphanumeric' },
    { id: 'events', header: 'Events', accessorFn: (endpoint) => endpoint.events.length, sortFn: 'basic' },
    { id: 'added', header: 'Added', accessorFn: (endpoint) => endpoint.createdAt, sortFn: 'basic', sortDescFirst: true },
  ];
  const sort = createTableSort('webhooks', { id: 'name', desc: false }, ['status', 'name', 'url', 'events', 'added']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return list;
    },
    getRowId: (endpoint) => endpoint.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));
  const href = (id: string) => `/settings/webhooks/${id}`;
  const events = (count: number) => (count === 1 ? '1 event' : `${count} events`);
</script>

<svelte:head>
  <title>Webhooks · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Webhooks' }]} />

<main class={[sidebar.isMobile && 'pb-28']}>
  <HeaderBand
    title="Webhooks"
    count={endpoints.isSuccess ? list.length : undefined}
    countLabel={list.length === 1 ? 'endpoint' : 'endpoints'}
    actions={sidebar.isMobile ? undefined : addButton}
  />

  <SettingsMenu>
    {#if endpoints.isError}
      <Panel title="Endpoints">
        <QueryError message={describeError(endpoints.error, 'Could not load the webhooks')} onretry={() => endpoints.refetch()} inset />
      </Panel>
    {:else}
      <Panel title="Endpoints" flush>
        {#if endpoints.isPending}
          <div aria-hidden="true" class="flex flex-col">
            {#each [0, 1] as row (row)}
              <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                <Skeleton class="h-4 w-16" />
                <Skeleton class="h-4 min-w-0 flex-1" />
                <Skeleton class="h-4 w-24" />
              </div>
            {/each}
          </div>
          <p class="sr-only" role="status">Loading the webhooks…</p>
        {:else if list.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><WebhookIcon /></Empty.Media>
              <Empty.Title>No webhooks yet</Empty.Title>
            </Empty.Header>
            <Empty.Content><Button href="/settings/webhooks/new" variant="outline" size="lg"><PlusIcon aria-hidden="true" />Add endpoint</Button></Empty.Content>
          </Empty.Root>
        {:else}
          <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
          <ul class="md:hidden">
            {#each sorted as endpoint (endpoint.id)}
              <ListItem href={href(endpoint.id)} title={name(endpoint)}>
                {#snippet status()}{@render badge(endpoint)}{/snippet}
                {#snippet facts()}
                  {#if endpoint.description}<Badge variant="muted" class="max-w-full"><span class="truncate">{endpoint.url}</span></Badge>{/if}
                  <Badge variant="muted">{events(endpoint.events.length)}</Badge>
                {/snippet}
              </ListItem>
            {/each}
          </ul>
          <div class="overflow-x-auto max-md:hidden">
            <Table.Root>
              <Table.Header>
                {#each table.getHeaderGroups() as group (group.id)}
                  <Table.Row class="bg-muted hover:bg-muted">
                    {#each group.headers as header (header.id)}
                      <SortHeader {header} class={header.id === 'status' ? 'px-4.5' : header.id === 'added' ? 'pr-4.5' : undefined} />
                    {/each}
                  </Table.Row>
                {/each}
              </Table.Header>
              <Table.Body>
                {#each sorted as endpoint (endpoint.id)}
                  <!-- The whole row is the link (design system section 9): the endpoint's link covers it. -->
                  <Table.Row class="relative h-12">
                    <Table.Cell class="px-4.5">{@render badge(endpoint)}</Table.Cell>
                    <Table.Cell class="w-1/2 max-w-0">
                      <TruncatedText
                        text={name(endpoint)}
                        href={href(endpoint.id)}
                        class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline"
                      />
                    </Table.Cell>
                    <!-- The URL gets its own cell only when a description names the endpoint, so it shows once (W6). -->
                    <Table.Cell class="w-1/2 max-w-0 text-muted-foreground">
                      {#if endpoint.description}<span class="relative block min-w-0"><TruncatedText text={endpoint.url} /></span>{/if}
                    </Table.Cell>
                    <Table.Cell class="tabular-nums">{events(endpoint.events.length)}</Table.Cell>
                    <Table.Cell class="pr-4.5 text-muted-foreground"><span class="relative"><DateText at={endpoint.createdAt} /></span></Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    {/if}

    <Panel title="How webhooks work">
      <div class="flex flex-col gap-4">
        <p class="max-w-measure">
          When something happens, ScreenHire posts a signed JSON message to each endpoint that listens for it, so another system
          hears about it without asking.
        </p>
        <SummaryList>
          <SummaryItem label="Events">
            {#if eventTypes.isSuccess}
              <ul class="flex flex-col gap-1">
                {#each eventTypes.data.data as type (type)}
                  <li>{eventLabel(type)} <span class="font-mono text-code text-muted-foreground">{type}</span></li>
                {/each}
              </ul>
            {:else if eventTypes.isError}
              <span class="text-muted-foreground">Could not load the events.</span>
            {:else}<Skeleton class="h-4 w-48" />{/if}
          </SummaryItem>
          <SummaryItem label="Check each message">
            <span class="flex flex-col gap-1">
              <span class="font-mono text-code break-all">ScreenHire-Signature: t=…,v1=…</span>
              <span>
                v1 is the HMAC-SHA256 of t, a full stop and the raw body, keyed with the endpoint's signing secret. Work out your own
                and compare.
              </span>
            </span>
          </SummaryItem>
          <SummaryItem label="Repeats">ScreenHire-Event-Id stays the same when a message is sent again, so you can drop repeats.</SummaryItem>
          <SummaryItem label="When delivery fails">
            ScreenHire tries again with a growing wait. After repeated failures the endpoint turns off and says why. Replay any delivery
            from its log.
          </SummaryItem>
          <SummaryItem label="Reference">
            {#if apiOrigin}
              <a href={`${apiOrigin}/scalar#tag/webhooks`} target="_blank" rel="noreferrer" class="inline-flex items-center gap-1.5 text-primary">
                <ExternalLinkIcon aria-hidden="true" class="shrink-0" /><span class="underline underline-offset-2">Webhooks in the API reference</span> <span class="sr-only">(opens in a new tab)</span>
              </a>
            {:else if meta.isError}
              <span class="text-muted-foreground">Could not find the API's address.</span>
            {:else}<Skeleton class="h-4 w-48" />{/if}
          </SummaryItem>
        </SummaryList>
      </div>
    </Panel>
  </SettingsMenu>

  {#if sidebar.isMobile}
    <ActionBar>{@render addButton()}</ActionBar>
  {/if}
</main>

<!-- Calm states are quiet in a list; an endpoint the worker turned off needs someone, so it is filled (T3). -->
{#snippet badge(endpoint: WebhookEndpointEntry)}
  {@const status = endpointStatus(endpoint)}
  <StatusBadge status={status.status} label={status.label} quiet={!needsAttention(status.status)} />
{/snippet}

{#snippet addButton()}
  <Button href="/settings/webhooks/new" size="lg"><PlusIcon aria-hidden="true" />Add endpoint</Button>
{/snippet}
