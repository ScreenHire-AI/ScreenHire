<script lang="ts">
  import CalendarIcon from '~icons/lucide/calendar';
  import UnlinkIcon from '~icons/lucide/unlink';
  import type { CalendarProvider, CalendarStatus } from '@screenhire/api-client';
  import { createDisconnectCalendar, createStartCalendarConnect } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import { calendarProblems } from '#lib/calendar.js';
  import FailureText from '#lib/components/failure-text.svelte';
  import Notice from '#lib/components/notice.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';

  // A person's own calendar (docs/architecture/interview-scheduling.md), opened from their menu: connect Google, Outlook
  // or the sandbox calendar through the provider's consent page, see which account it is, or disconnect it. Only the
  // providers this ScreenHire has configured are offered. Every member can do it; nothing here needs a permission.
  let {
    open = $bindable(false),
    status,
    outcome = '',
  }: {
    open?: boolean;
    status: CalendarStatus;
    /** What the last round trip said, when it failed. */
    outcome?: string;
  } = $props();

  const queryClient = useQueryClient();
  const settled = () => invalidatePath(queryClient, '/v1/calendar');
  const connect = createStartCalendarConnect();
  const disconnect = createDisconnectCalendar(() => ({ mutation: { onSettled: settled } }));

  const connection = $derived(status.connection);
  let failure = $state('');
  let saved = $state('');
  let connecting = $state(false);

  $effect(() => {
    if (open) {
      failure = outcome;
      saved = '';
    }
  });

  async function connectCalendar(provider: CalendarProvider) {
    failure = '';
    connecting = true;
    try {
      const response = await connect.mutateAsync({ data: { provider, returnUrl: `${page.url.pathname}${page.url.search}` } });
      window.location.assign(response.data.authorizeUrl);
    } catch (error) {
      failure = describeError(error, 'Could not start connecting your calendar');
      connecting = false;
    }
  }

  async function disconnectCalendar() {
    failure = '';
    try {
      await disconnect.mutateAsync();
      saved = 'Your calendar is disconnected.';
    } catch (error) {
      failure = describeError(error, 'Could not disconnect your calendar');
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Calendar</Sheet.Title>
    </Sheet.Header>
    <div class="flex flex-col gap-5 px-4 pb-4">
      {#if !connection}
        <p>Connect your calendar so ScreenHire offers candidates times you are free, and puts their interview in it.</p>
        <div class="flex flex-wrap gap-2">
          {#each status.providers as provider (provider.id)}
            <Button size="lg" variant={status.providers.length === 1 ? 'default' : 'outline'} disabled={connecting} onclick={() => connectCalendar(provider.id)}>
              <CalendarIcon aria-hidden="true" />Connect {provider.name}
            </Button>
          {/each}
        </div>
      {:else}
        {#if connection.problem}<Notice>{calendarProblems[connection.problem]}</Notice>{/if}
        <!-- The state first (T2), then which calendar. Needs fixes, not Connected, while ScreenHire is refused. -->
        <div class="flex flex-col items-start gap-2">
          {#if connection.problem}
            <StatusBadge status="needsFixes" label="Needs connecting again" />
          {:else}
            <StatusBadge status="done" label="Connected" />
          {/if}
          <p class="flex min-w-0 flex-col">
            <span class="font-semibold">{connection.providerName}</span>
            {#if connection.accountEmail}<span class="text-muted-foreground">{connection.accountEmail}</span>{/if}
          </p>
        </div>
        <div class="flex flex-wrap gap-2">
          {#if connection.problem}
            <Button size="lg" disabled={connecting} onclick={() => connectCalendar(connection.provider)}>
              <CalendarIcon aria-hidden="true" />Connect again
            </Button>
          {/if}
          <Button variant="outline" size="lg" disabled={disconnect.isPending} onclick={disconnectCalendar}>
            <UnlinkIcon aria-hidden="true" />Disconnect
          </Button>
        </div>
        <!-- How far Disconnect reaches: it ends ScreenHire's access, and leaves what is already on the calendar. -->
        <p class="text-muted-foreground">Disconnecting stops ScreenHire offering times from it. Interviews already booked stay on your calendar.</p>
      {/if}
      <FailureText message={failure} />
      <p role="status" class="text-muted-foreground empty:hidden">{failure ? '' : saved}</p>
    </div>
  </Sheet.Content>
</Sheet.Root>
