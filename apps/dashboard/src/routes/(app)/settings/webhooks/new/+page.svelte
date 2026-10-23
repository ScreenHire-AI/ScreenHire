<script lang="ts">
  import { createCreateWebhookEndpoint } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import HeaderBand from '#lib/components/header-band.svelte';
  import SecretDialog from '#lib/components/secret-dialog.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import WebhookEndpointForm from '#lib/components/webhook-endpoint-form.svelte';
  import { invalidatePath } from '#lib/query.js';

  // A new webhook endpoint, on the form template. The API answers with its signing secret, shown once here before the
  // endpoint's page opens.
  const queryClient = useQueryClient();
  const create = createCreateWebhookEndpoint(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, '/v1/webhooks') } }));

  let secret = $state('');
  let secretOpen = $state(false);
  let createdId = '';

  async function save(value: { url: string; description: string | null; events: string[] }) {
    const response = await create.mutateAsync({ data: value });
    createdId = response.data.endpoint.id;
    secret = response.data.signingSecret;
    secretOpen = true;
  }

  function opened() {
    secret = '';
    create.reset();
    if (createdId) void goto(`/settings/webhooks/${createdId}`);
  }
</script>

<svelte:head>
  <title>Add endpoint · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Webhooks', href: '/settings/webhooks' }, { label: 'Add endpoint' }]} back />

<main class="flex flex-col">
  <HeaderBand form title="Add endpoint" />
  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    <WebhookEndpointForm submitLabel="Add endpoint" cancelHref="/settings/webhooks" failure="Could not add the endpoint" onsave={save} />
  </div>
</main>

<SecretDialog bind:open={secretOpen} title="Your signing secret" label="Signing secret" value={secret} onclose={opened} />
