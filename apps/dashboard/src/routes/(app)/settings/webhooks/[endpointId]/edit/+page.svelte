<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import { ApiError, createGetWebhookEndpoint, createUpdateWebhookEndpoint } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import WebhookEndpointForm from '#lib/components/webhook-endpoint-form.svelte';
  import { describeError, invalidatePath } from '#lib/query.js';
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
  <HeaderBand form title="Edit endpoint" />

  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    {#if endpoint.isError && !detail}
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
    {:else if !detail}
      <Panel title="Endpoint">
        <div aria-hidden="true" class="flex flex-col gap-4">
          {#each [0, 1, 2] as field (field)}<Skeleton class="h-14 w-full" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the endpoint…</p>
      </Panel>
    {:else}
      <WebhookEndpointForm endpoint={detail} submitLabel="Save" cancelHref={endpointHref} failure="Could not save the endpoint" onsave={save} />
    {/if}
  </div>
</main>
