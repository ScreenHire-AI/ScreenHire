<script lang="ts">
  import BellIcon from '~icons/lucide/bell';
  import XIcon from '~icons/lucide/x';
  import type { CurrentUser } from '@screenhire/api-client';
  import {
    createDismissNotification,
    createListNotifications,
    createMarkAllNotificationsRead,
    createMarkNotificationRead,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { mergeProps } from 'bits-ui';
  import IconButton from '#lib/components/icon-button.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { notificationLine } from '#lib/notification-lines.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { notificationsPath } from '#lib/realtime.js';
  import { setupPanel } from '#lib/setup-panel.svelte.js';
  import { createSetupProgress } from '#lib/setup-progress.svelte.js';

  // The notification bell in the top bar (design system section 11, Notification bell): what needs this person's
  // attention. The setup steps still to do come first, for whoever can do them, then the alerts the server holds for them,
  // one plain line each that links to the page that fixes it. The count is the steps left plus the alerts not yet read.
  // Opening the panel marks nothing read; following an alert's link does. Alerts arrive live (`NotificationsChanged`
  // refetches the list), and a missed push costs nothing because the list is the server's, read again on reconnect.
  // The panel also opens at its setup section when a page asks (`setupPanel`, the Jobs empty state's button), and once by
  // itself right after onboarding (`preferences.takeSetupTour`), as soon as the steps are known.
  let { user }: { user: CurrentUser | null | undefined } = $props();

  const queryClient = useQueryClient();
  const notifications = createListNotifications(() => ({ query: { staleTime: 30_000 } }));
  const setup = createSetupProgress(() => user);
  const refresh = () => invalidatePath(queryClient, notificationsPath);
  const markRead = createMarkNotificationRead(() => ({ mutation: { onSettled: refresh } }));
  const markAllRead = createMarkAllNotificationsRead(() => ({ mutation: { onSettled: refresh } }));
  const dismiss = createDismissNotification(() => ({ mutation: { onSettled: refresh } }));

  let open = $state(false);
  let panel = $state<HTMLElement | null>(null);
  // Set when the panel opens for its setup steps, so focus starts there and not on the panel's top, until it closes:
  // bits-ui can ask where focus goes more than once while the panel opens. The section is found in the panel each time.
  let focusSetup = false;
  let handledRequests = setupPanel.requests;

  $effect(() => {
    if (!open) focusSetup = false;
  });
  $effect(() => {
    if (setupPanel.requests > handledRequests) {
      handledRequests = setupPanel.requests;
      focusSetup = true;
      // Already open: no autofocus follows, so move focus to the steps here.
      if (open) panel?.querySelector<HTMLElement>('[data-bell-setup]')?.focus();
      open = true;
    }
  });
  // The one-time open after onboarding is taken only once the steps are known and some remain, so a bell that unmounts
  // before then (the person moved on while the steps loaded) leaves it for the next one.
  $effect(() => {
    if (user?.id && setup.steps && setup.remaining.length > 0 && preferences.takeSetupTour(user.id)) {
      focusSetup = true;
      open = true;
    }
  });

  const items = $derived(notifications.data?.data.items ?? []);
  const unread = $derived(notifications.data?.data.unreadCount ?? 0);
  const count = $derived(setup.remaining.length + unread);
  const label = $derived(count > 0 ? `Notifications, ${count} need attention` : 'Notifications');
  const empty = $derived(notifications.isSuccess && items.length === 0 && setup.remaining.length === 0);
  // The step's place in the whole list, so a step keeps its number as the ones before it are done.
  const numbered = $derived((setup.steps ?? []).map((step, index) => ({ step, number: index + 1 })).filter(({ step }) => !step.done));
</script>

<Popover.Root bind:open>
  <Tooltip.Root disabled={open}>
    <Tooltip.Trigger>
      {#snippet child({ props: tooltipProps })}
        <Popover.Trigger>
          {#snippet child({ props })}
            <Button {...mergeProps(tooltipProps, props)} variant="outline" size="icon-lg" aria-label={label} class="relative shrink-0">
              <BellIcon aria-hidden="true" />
              {#if count > 0}
                <Badge variant="count" aria-hidden="true" data-bell-count class="absolute -top-2 -right-2.5 min-w-5 border-input px-1.5">{count > 99 ? '99+' : count}</Badge>
              {/if}
            </Button>
          {/snippet}
        </Popover.Trigger>
      {/snippet}
    </Tooltip.Trigger>
    <!-- A new Owner has no reason to open a bell, so while setup steps are left the tooltip says the bell holds them. -->
    <Tooltip.Content>{setup.steps ? 'Setup and notifications' : 'Notifications'}</Tooltip.Content>
  </Tooltip.Root>

  <!-- Focus starts on the panel, not its first button: that would open its tooltip over the list. -->
  <Popover.Content
    bind:ref={panel}
    onOpenAutoFocus={(event) => {
      event.preventDefault();
      const setupSection = focusSetup ? panel?.querySelector<HTMLElement>('[data-bell-setup]') : null;
      (setupSection ?? panel)?.focus();
    }}
    align="end"
    collisionPadding={16}
    role="dialog"
    aria-label="Needs attention"
    class="max-h-[min(36rem,calc(100dvh-5rem))] w-95 max-w-[calc(100vw-2rem)] gap-0 overflow-y-auto p-0">
    <header class="flex h-14 shrink-0 items-center gap-2 border-b border-border-subtle px-4">
      <h2 class="min-w-0 flex-1 truncate text-heading">Needs attention</h2>
      {#if unread > 0}
        <Button variant="outline" onclick={() => markAllRead.mutate()} disabled={markAllRead.isPending}>Mark all as read</Button>
      {/if}
    </header>

    {#if setup.steps}
      <section data-bell-setup tabindex="-1" aria-labelledby="bell-setup-title" class="flex flex-col gap-1 outline-hidden border-b border-border-subtle px-4 py-3">
        <div class="flex items-center gap-2">
          <h3 id="bell-setup-title" class="min-w-0 flex-1 truncate font-semibold">Finish setting up</h3>
          <IconButton label="Hide setup in this browser" onclick={() => setup.dismiss()}><XIcon aria-hidden="true" /></IconButton>
        </div>
        <ol aria-label="Setup steps" class="flex flex-col">
          {#each numbered as { step, number } (step.id)}
            <li class="flex min-w-0 items-center gap-3 py-1.5">
              <StepMarker state="upcoming">{number}</StepMarker>
              <a href={step.href} onclick={() => (open = false)} class="block min-w-0 truncate py-0.5 font-semibold text-foreground underline underline-offset-2 pointer-coarse:py-3">{step.label}</a>
              {#if step.optional}<span class="shrink-0 text-caption text-muted-foreground">Optional</span>{/if}
            </li>
          {/each}
        </ol>
        {#if setup.doneCount > 0}
          <p class="text-body text-muted-foreground">{setup.doneCount} {setup.doneCount === 1 ? 'step' : 'steps'} done</p>
        {/if}
      </section>
    {/if}

    {#if notifications.isError}
      <div class="p-4">
        <QueryError message={describeError(notifications.error, 'Could not load notifications')} onretry={() => notifications.refetch()} inset />
      </div>
    {:else if notifications.isPending && !setup.steps}
      <div class="flex flex-col gap-3 p-4" aria-busy="true">
        <Skeleton class="h-5 w-4/5" />
        <Skeleton class="h-5 w-3/5" />
      </div>
    {:else if items.length > 0}
      <ul aria-label="Alerts" class="flex flex-col">
        {#each items as item (item.id)}
          {@const line = notificationLine(item.kind, item.payload)}
          <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle py-2.5 pr-2 pl-4 last:border-b-0">
            <div class="flex min-w-0 flex-1 flex-col gap-0.5">
              <a
                href={item.link}
                onclick={() => {
                  if (!item.readAt) markRead.mutate({ id: item.id });
                  open = false;
                }}
                class={['min-w-0 break-words text-foreground underline underline-offset-2 pointer-coarse:py-3', !item.readAt && 'font-semibold']}
              >
                {#if !item.readAt}<span class="sr-only">Unread:</span>{' '}{/if}{line}
              </a>
              <RelativeTime at={item.createdAt} class="text-caption text-muted-foreground" />
            </div>
            <IconButton label={`Dismiss: ${line}`} onclick={() => dismiss.mutate({ id: item.id })}><XIcon aria-hidden="true" /></IconButton>
          </li>
        {/each}
      </ul>
    {:else if empty}
      <p class="px-4 py-6 text-body text-muted-foreground">Nothing needs attention.</p>
    {/if}
  </Popover.Content>
</Popover.Root>
