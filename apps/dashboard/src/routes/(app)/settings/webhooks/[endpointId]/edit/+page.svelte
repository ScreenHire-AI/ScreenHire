<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import { ApiError, createGetWebhookEndpoint, createUpdateWebhookEndpoint } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import WebhookEndpointForm from '#lib/components/webhook-endpoint-form.svelte';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { endpointStatus } from '#lib/webhooks.js';
  import type { PageProps } from './$types';

  // A webhook endpoint's address, description and events, on the form template. Saving leaves it on or off as it is.
  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  const endpoint = createGetWebhookEndpoint(() => params.endpointId);
  const update = createUpdateWebhookEndpoint(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, '/v1/webhooks') } }));
  const detail = $derived(endpoint.data?.data);
  const notFound = $derived(endpoint.error instanceof ApiError && endpoint.error.status === 404);
  const endpointHref = $derived(`/settings/webhooks/${params.endpointId}`);

  async function save(value: { url: string; description: string | null; events: string[] }) {
    await update.mutateAsync({ endpointId: params.endpointId, data: value });
    await goto(endpointHref);
  }
</script>

<svelte:head>
  <title>Edit endpoint · ScreenHire</title>
</svelte:head>

<TopBar
  crumbs={[
    { label: 'Settings' },
    { label: 'Webhooks', href: '/settings/webhooks' },
    { label: detail?.description ?? detail?.url ?? 'Endpoint', href: endpointHref },
    { label: 'Edit' },
  ]}
  back
/>

<main class="flex flex-col">
  <!-- The endpoint's name and its state first (S2), as Edit details is headed with the job's; the crumb says what the page does. -->
  <HeaderBand form title={detail ? (detail.description ?? detail.url) : 'Edit endpoint'}>
    {#snippet status()}{#if detail}<StatusBadge {...endpointStatus(detail)} />{/if}{/snippet}
  </HeaderBand>

  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    {#if endpoint.isError && !detail}
      <Panel title={notFound ? 'Endpoint not found' : undefined} label="Endpoint">
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
    {:else if !detail}
      <Panel title="Endpoint">
        <div aria-hidden="true" class="flex flex-col gap-4">
          {#each [0, 1, 2] as field (field)}<Skeleton class="h-14 w-full" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the endpoint…</p>
      </Panel>
    {:else}
      {#if !detail.enabled}
        <!-- Saving leaves it off, and the page says so where someone is looking at the form (E3); Turn on is on the endpoint's page. -->
        <Notice href={endpointHref} link="Turn it on there" class="mb-5">This endpoint is off. Saving does not turn it on.</Notice>
      {/if}
      <WebhookEndpointForm endpoint={detail} submitLabel="Save" cancelHref={endpointHref} failure="Could not save the endpoint" onsave={save} />
    {/if}
  </div>
</main>
