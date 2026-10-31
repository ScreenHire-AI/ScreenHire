<script lang="ts">
  import SaveIcon from '@lucide/svelte/icons/save';
  import type { ToastKind } from '@screenhire/api-client';
  import { createGetNotificationSettings, createUpdateNotificationSettings } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import { Label } from '#lib/components/ui/label/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { toastLifetime } from '#lib/notifications.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // The person's own settings for teammates' changes as toasts (docs/architecture/realtime.md), from the account menu.
  // They live on the server, per organization, so they follow the person to another browser. The kinds hide while toasts
  // are off: there is nothing to choose then (A3). Mentions and automation cards are not toasts and always show.
  let { open = $bindable(false), onsaved }: { open?: boolean; onsaved?: (announcement: string) => void } = $props();

  const settingsPath = '/v1/notification-settings';
  const queryClient = useQueryClient();
  const settings = createGetNotificationSettings(() => ({ query: { enabled: open } }));
  const update = createUpdateNotificationSettings(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, settingsPath) } }));

  const groups: readonly { label: string; kinds: readonly { kind: ToastKind; label: string }[] }[] = [
    {
      label: 'Candidates on your jobs',
      kinds: [
        { kind: 'Moves', label: 'Moves' },
        { kind: 'NewCandidates', label: 'New candidates' },
        { kind: 'Notes', label: 'Notes' },
        { kind: 'Tags', label: 'Tags' },
        { kind: 'Emails', label: 'Emails' },
        { kind: 'Scorecards', label: 'Scorecards' },
      ],
    },
    { label: 'Your tasks', kinds: [{ kind: 'Tasks', label: 'Tasks given to you' }] },
    { label: 'Your templates', kinds: [{ kind: 'Templates', label: 'Templates you last changed' }] },
  ];

  let toasts = $state(true);
  let closeToasts = $state(true);
  let off = $state<ToastKind[]>([]);
  let failure = $state('');
  let filled = $state(false);

  // The draft fills once each time the sheet opens, so a refetch while it is open leaves the person's choices alone.
  $effect(() => {
    if (!open) {
      filled = false;
      return;
    }
    const data = settings.data?.data;
    if (data && !filled) {
      toasts = data.toasts;
      closeToasts = data.closeToasts;
      off = [...data.off];
      failure = '';
      filled = true;
    }
  });

  function setKind(kind: ToastKind, on: boolean) {
    off = on ? off.filter((each) => each !== kind) : [...off, kind];
  }

  async function save(event: SubmitEvent) {
    event.preventDefault();
    failure = '';
    try {
      await update.mutateAsync({ data: { toasts, closeToasts, off } });
      open = false;
      onsaved?.('Your notification settings were saved.');
    } catch (error) {
      failure = describeError(error, 'Could not save your notification settings');
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Notifications</Sheet.Title>
      <Sheet.Description>Toasts when a teammate changes something you work on.</Sheet.Description>
    </Sheet.Header>
    {#if settings.isError}
      <div class="px-4 pb-4">
        <QueryError message={describeError(settings.error, 'Could not load your notification settings')} onretry={() => settings.refetch()} inset />
      </div>
    {:else if !filled}
      <div class="flex flex-col gap-3 px-4 pb-4"><Skeleton class="h-10 w-full" /><Skeleton class="h-40 w-full" /></div>
    {:else}
      <form onsubmit={save} novalidate class="flex flex-col gap-5 px-4 pb-4">
        <div class="flex items-center justify-between gap-4 rounded-lg bg-secondary px-3 py-2.5">
          <Label for="notifications-toasts" class="font-semibold">Show toasts</Label>
          <Switch id="notifications-toasts" bind:checked={toasts} />
        </div>

        {#if toasts}
          {#each groups as group (group.label)}
            <fieldset class="flex flex-col gap-1">
              <legend class="mb-1 text-caption font-semibold text-muted-foreground">{group.label}</legend>
              {#each group.kinds as choice (choice.kind)}
                <div class="flex min-h-10 items-center justify-between gap-4 pointer-coarse:min-h-11">
                  <Label for={`notifications-${choice.kind}`} class="font-normal">{choice.label}</Label>
                  <Switch
                    id={`notifications-${choice.kind}`}
                    checked={!off.includes(choice.kind)}
                    onCheckedChange={(checked) => setKind(choice.kind, checked)}
                  />
                </div>
              {/each}
            </fieldset>
          {/each}

          <div class="flex items-start justify-between gap-4">
            <div class="flex flex-col gap-0.5">
              <Label for="notifications-close">Close automatically</Label>
              <span id="notifications-close-description" class="text-caption text-muted-foreground">After {toastLifetime / 1000} seconds</span>
            </div>
            <Switch id="notifications-close" bind:checked={closeToasts} aria-describedby="notifications-close-description" />
          </div>
        {/if}

        <FailureText message={failure} />
        <div class="flex justify-end gap-2">
          <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
          <Button type="submit" size="lg" disabled={update.isPending}><SaveIcon aria-hidden="true" />Save</Button>
        </div>
      </form>
    {/if}
  </Sheet.Content>
</Sheet.Root>
