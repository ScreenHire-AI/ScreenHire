<script lang="ts">
  import PlusIcon from '@lucide/svelte/icons/plus';
  import WebhookIcon from '@lucide/svelte/icons/webhook';
  import type { WebhookEndpointEntry } from '@screenhire/api-client';
  import { createListWebhookEndpoints } from '@screenhire/api-client/svelte-query';
  import ActionBar from '#lib/components/action-bar.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
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
  import { endpointStatus } from '#lib/webhooks.js';

  // The organization's webhook endpoints (workflow-engine.md section 8), on the list template. Each row opens the
  // endpoint: its events, delivery log and replay.
  const sidebar = Sidebar.useSidebar();
  const endpoints = createListWebhookEndpoints();
  const list = $derived(endpoints.data?.data ?? []);
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

  <div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6">
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
              <Empty.Description>An endpoint gets a signed request when a job or an application changes.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button href="/settings/webhooks/new" variant="outline" size="lg"><PlusIcon aria-hidden="true" />Add endpoint</Button></Empty.Content>
          </Empty.Root>
        {:else}
          <ul class="md:hidden">
            {#each list as endpoint (endpoint.id)}
              <ListItem href={href(endpoint.id)} title={endpoint.description ?? endpoint.url}>
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
                <Table.Row class="bg-muted hover:bg-muted">
                  <Table.Head class="px-4.5">Status</Table.Head>
                  <Table.Head>Endpoint</Table.Head>
                  <Table.Head>Events</Table.Head>
                  <Table.Head class="pr-4.5 text-right">Added</Table.Head>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {#each list as endpoint (endpoint.id)}
                  <!-- The whole row is the link (design system section 9): the endpoint's link covers it. -->
                  <Table.Row class="relative h-12">
                    <Table.Cell class="px-4.5">{@render badge(endpoint)}</Table.Cell>
                    <Table.Cell class="w-full max-w-0">
                      <span class="flex min-w-0 flex-col">
                        <TruncatedText
                          text={endpoint.description ?? endpoint.url}
                          href={href(endpoint.id)}
                          class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline"
                        />
                        {#if endpoint.description}<span class="relative min-w-0 text-caption text-muted-foreground"><TruncatedText text={endpoint.url} /></span>{/if}
                      </span>
                    </Table.Cell>
                    <Table.Cell class="tabular-nums">{events(endpoint.events.length)}</Table.Cell>
                    <Table.Cell class="pr-4.5 text-right text-muted-foreground"><span class="relative"><DateText at={endpoint.createdAt} /></span></Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    {/if}
  </div>

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
