<script lang="ts">
  import HashIcon from '@lucide/svelte/icons/hash';
  import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
  import SaveIcon from '@lucide/svelte/icons/save';
  import SendIcon from '@lucide/svelte/icons/send';
  import UnplugIcon from '@lucide/svelte/icons/unplug';
  import { type HttpValidationProblemDetails, SlackChannelEvent } from '@screenhire/api-client';
  import {
    ApiError,
    createDisconnectSlack,
    createGetSlack,
    createListSlackChannels,
    createSendSlackTestMessage,
    createStartSlackInstall,
    createUpdateSlackChannel,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import Picker from '#lib/components/picker.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { slackEvents, slackFailure, slackProblems, workspaceStatus } from '#lib/slack.js';

  // Settings > Slack (docs/architecture/slack.md), on the settings template: connect the organization's workspace,
  // pick the channel and what posts there, and disconnect it. Each person links their own account from their menu
  // (SlackAccountSheet), so this page is the workspace's alone. Disconnecting is for good, so it confirms (A2).
  const slackPath = '/v1/slack';
  const queryClient = useQueryClient();
  const settled = () => invalidatePath(queryClient, slackPath);

  const slack = createGetSlack();
  const current = $derived(slack.data?.data);
  const settings = $derived(current?.settings ?? undefined);
  const connected = $derived(Boolean(current?.workspace && settings));
  const channels = createListSlackChannels(() => ({ query: { enabled: connected && settings?.problem !== 'Disconnected' } }));
  const install = createStartSlackInstall();
  const update = createUpdateSlackChannel(() => ({ mutation: { onSettled: settled } }));
  const test = createSendSlackTestMessage(() => ({ mutation: { onSettled: settled } }));
  const disconnect = createDisconnectSlack(() => ({ mutation: { onSettled: settled } }));

  // How a round trip to Slack ended (`?slack=`), read once and taken out of the address, so a reload does not repeat it.
  let failure = $state(slackFailure(page.url.searchParams.get('slack')) ?? '');
  $effect(() => {
    if (page.url.searchParams.has('slack')) {
      const url = new URL(page.url.href);
      url.searchParams.delete('slack');
      void goto(url, { replace: true, reset: false });
    }
  });

  let connecting = $state(false);
  async function connect() {
    failure = '';
    connecting = true;
    try {
      const response = await install.mutateAsync();
      window.location.assign(response.data.authorizeUrl);
    } catch (error) {
      failure = describeError(error, 'Could not start connecting Slack');
      connecting = false;
    }
  }

  const schema = z.object({
    channelId: z.string().min(1, 'Pick a channel.'),
    events: z.array(z.enum(SlackChannelEvent)),
  });

  let saved = $state('');
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ channelId: '', events: [] as SlackChannelEvent[] }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      try {
        await update.mutateAsync({ data: { channelId: submitted.data.channelId, events: submitted.data.events } });
        saved = 'Saved.';
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the channel')];
        }
      }
    },
  });

  // The form starts from the saved settings once they load; a refetch after that keeps what was changed.
  let started = false;
  $effect(() => {
    if (settings && !started) {
      started = true;
      reset({ data: { channelId: settings.channel?.id ?? '', events: [...settings.events] } });
    }
  });

  const channelOptions = $derived(
    (channels.data?.data ?? []).map((channel) => ({ id: channel.id, label: channel.name, hint: channel.isPrivate ? 'Private' : undefined })),
  );
  const chosenName = $derived(
    channels.data?.data.find((channel) => channel.id === $form.channelId)?.name ??
      (settings?.channel?.id === $form.channelId ? settings?.channel?.name : undefined),
  );

  let announcement = $state('');
  let testFailure = $state('');
  async function sendTest() {
    testFailure = '';
    try {
      const response = await test.mutateAsync();
      announcement = response.data.posted ? 'The test message is in the channel.' : '';
      if (!response.data.posted && response.data.problem) testFailure = slackProblems[response.data.problem];
    } catch (error) {
      testFailure = describeError(error, 'Could not send the test message');
    }
  }

  // Disconnect confirms in a dialog that the More menu opens once it has closed.
  let disconnectOpen = $state(false);
  let chosen: 'disconnect' | 'test' | 'reconnect' | null = null;
  const onMenuClosed = () => {
    if (chosen === 'disconnect') disconnectOpen = true;
    else if (chosen === 'test') void sendTest();
    else if (chosen === 'reconnect') void connect();
    chosen = null;
  };

  async function disconnectSlack() {
    await disconnect.mutateAsync();
    started = false;
    announcement = 'Slack is disconnected.';
  }
</script>

<svelte:head>
  <title>Slack · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Slack' }]} />

<main class="flex flex-col">
  <HeaderBand title="Slack">
    {#snippet status()}
      {#if connected && settings}<StatusBadge {...workspaceStatus(settings.problem)} />{/if}
    {/snippet}
    {#snippet actions()}
      {#if connected && settings}
        <!-- Once Slack no longer accepts ScreenHire, Reconnect is the fix the notice names, so it leaves the menu (W7). -->
        {#if settings.problem === 'Disconnected'}
          <Button variant="outline" disabled={connecting} onclick={connect}><RefreshCwIcon aria-hidden="true" />Reconnect</Button>
        {/if}
        <MoreMenu label="More actions" onclosed={onMenuClosed}>
          {#if settings.channel && settings.problem !== 'Disconnected'}
            <DropdownMenu.Item onSelect={() => (chosen = 'test')}><SendIcon aria-hidden="true" />Send test message</DropdownMenu.Item>
          {/if}
          {#if settings.problem !== 'Disconnected'}
            <DropdownMenu.Item onSelect={() => (chosen = 'reconnect')}><RefreshCwIcon aria-hidden="true" />Reconnect</DropdownMenu.Item>
            <DropdownMenu.Separator />
          {/if}
          <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = 'disconnect')}><UnplugIcon aria-hidden="true" />Disconnect Slack</DropdownMenu.Item>
        </MoreMenu>
      {/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      <p role="status" class="sr-only">{announcement}</p>
      {#if slack.isError && !current}
        <Panel title="Workspace">
          <QueryError message={describeError(slack.error, 'Could not load Slack')} onretry={() => slack.refetch()} inset />
        </Panel>
      {:else if !current}
        <Panel title="Workspace">
          <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-6 w-full max-w-96" /><Skeleton class="h-10 w-40" /></div>
          <p class="sr-only" role="status">Loading Slack…</p>
        </Panel>
      {:else if !current.available}
        <Panel title="Workspace">
          <Empty.Root>
            <Empty.Header>
              <Empty.Title>Slack is not set up here</Empty.Title>
              <Empty.Description>Slack needs a Slack app set up where ScreenHire is hosted. It cannot be set up from this page.</Empty.Description>
            </Empty.Header>
          </Empty.Root>
        </Panel>
      {:else if !connected || !settings}
        <Panel title="Workspace">
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>
                Post hiring events to a channel, and send each person their mentions and tasks as direct messages.
              </Empty.Description>
            </Empty.Header>
            <Empty.Content>
              <Button size="lg" disabled={connecting} onclick={connect}><HashIcon aria-hidden="true" />Add to Slack</Button>
              <FailureText message={failure} />
            </Empty.Content>
          </Empty.Root>
        </Panel>
      {:else}
        {#if settings.problem}
          <!-- A problem to fix (E2), at the top of the work area: what is wrong and what fixes it. -->
          <Notice>
            {slackProblems[settings.problem]}
            {#if settings.problemAt}Since <RelativeTime at={settings.problemAt} />.{/if}
          </Notice>
        {/if}

        <!-- Which workspace and who added it, in the work area: a settings page's header stays one row, so moving between
             settings pages never shifts the menu (S8). The row holds the bot token, so it has no History (T6, ADR-0016). -->
        {#if current.workspace}
          <Panel title="Workspace">
            <SummaryList>
              <SummaryItem label="Workspace"><TruncatedText text={current.workspace.teamName} /></SummaryItem>
              <SummaryItem label="Added"><RelativeTime at={settings.connectedAt} />{settings.connectedBy ? ` by ${settings.connectedBy}` : ''}</SummaryItem>
            </SummaryList>
          </Panel>
        {/if}

        <form method="POST" use:enhance novalidate aria-label="Channel">
          <Panel title="Channel">
            <div class="flex flex-col gap-5">
              <p class="max-w-measure text-muted-foreground">
                Everyone in the channel sees these, whatever jobs they are on. Verification results stay in ScreenHire.
              </p>

              <Field.Field data-invalid={$errors.channelId ? true : undefined}>
                <span class="sr-only" id="slack-channel-label">Channel</span>
                <div class="flex min-w-0 flex-wrap items-center gap-3" aria-labelledby="slack-channel-label" role="group">
                  {#if chosenName}<span class="min-w-0 truncate font-semibold">#{chosenName}</span>{/if}
                  {#if channels.isError}
                    <FailureText message={describeError(channels.error, 'Could not load the channels')} />
                  {:else}
                    <Picker
                      label={chosenName ? 'Change channel' : 'Pick a channel'}
                      icon={HashIcon}
                      options={channelOptions}
                      searchLabel="Search channels"
                      empty={channels.isPending ? 'Loading the channels…' : 'No channel matches.'}
                      onpick={(id) => ($form.channelId = id)}
                    />
                  {/if}
                </div>
                <Field.Error id="slack-channel-error" errors={$errors.channelId?.map((message) => ({ message }))} />
              </Field.Field>

              <fieldset class="flex flex-col gap-2">
                <legend class="mb-1 text-body font-medium">Post when</legend>
                {#each slackEvents as option (option.event)}
                  <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
                    <input type="checkbox" name="events" value={option.event} bind:group={$form.events} class="size-4 shrink-0 accent-primary" />
                    {option.label}
                  </label>
                {/each}
              </fieldset>
            </div>
            {#snippet footer()}
              <div class="flex flex-wrap items-center justify-end gap-2">
                <div class="mr-auto min-w-0">
                  <FailureText message={$errors._errors?.[0] || testFailure || failure} />
                  <p role="status" class="text-muted-foreground empty:hidden">{$errors._errors || testFailure || failure ? '' : saved}</p>
                </div>
                <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
              </div>
            {/snippet}
          </Panel>
        </form>
      {/if}
    </div>
  </SettingsMenu>
</main>

<ConfirmDialog
  bind:open={disconnectOpen}
  title="Disconnect Slack?"
  description="Nothing more posts to the channel, and everyone's linked Slack account is unlinked. You can connect again later."
  cancelLabel="Keep it"
  confirmLabel="Disconnect Slack"
  failure="Could not disconnect Slack"
  onconfirm={disconnectSlack}
>
  {#snippet icon()}<UnplugIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>
