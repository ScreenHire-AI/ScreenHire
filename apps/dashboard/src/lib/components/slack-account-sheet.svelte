<script lang="ts">
  import HashIcon from '@lucide/svelte/icons/hash';
  import UnlinkIcon from '@lucide/svelte/icons/unlink';
  import type { SlackAccount, SlackStatus } from '@screenhire/api-client';
  import { createStartSlackLink, createUnlinkSlackAccount, createUpdateSlackAccount } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { page } from '$app/state';
  import FailureText from '#lib/components/failure-text.svelte';
  import Notice from '#lib/components/notice.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { slackProblems } from '#lib/slack.js';

  // A person's own Slack account (docs/architecture/slack.md), opened from their menu: link it with Sign in with Slack,
  // choose which direct messages they get, or unlink it. Every member has it while the organization's workspace is
  // connected; managing the workspace is Settings > Slack. Each switch saves as it changes, with its result announced.
  let {
    open = $bindable(false),
    status,
    outcome = '',
  }: {
    open?: boolean;
    status: SlackStatus;
    /** What the last round trip to Slack said, when it failed. */
    outcome?: string;
  } = $props();

  const queryClient = useQueryClient();
  const settled = () => invalidatePath(queryClient, '/v1/slack');
  const link = createStartSlackLink();
  const update = createUpdateSlackAccount(() => ({ mutation: { onSettled: settled } }));
  const unlink = createUnlinkSlackAccount(() => ({ mutation: { onSettled: settled } }));

  const account = $derived(status.account);
  let failure = $state('');
  let saved = $state('');
  let linking = $state(false);

  $effect(() => {
    if (open) {
      failure = outcome;
      saved = '';
    }
  });

  async function linkAccount() {
    failure = '';
    linking = true;
    try {
      const response = await link.mutateAsync({ data: { returnUrl: `${page.url.pathname}${page.url.search}` } });
      window.location.assign(response.data.authorizeUrl);
    } catch (error) {
      failure = describeError(error, 'Could not start linking Slack');
      linking = false;
    }
  }

  async function change(next: Pick<SlackAccount, 'mentions' | 'tasks'>) {
    failure = '';
    saved = '';
    try {
      await update.mutateAsync({ data: { mentions: next.mentions, tasks: next.tasks } });
      saved = 'Saved.';
    } catch (error) {
      failure = describeError(error, 'Could not save your Slack messages');
    }
  }

  async function unlinkAccount() {
    failure = '';
    try {
      await unlink.mutateAsync();
      saved = 'Your Slack account is unlinked.';
    } catch (error) {
      failure = describeError(error, 'Could not unlink Slack');
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Slack account</Sheet.Title>
      {#if status.workspace}<Sheet.Description>{status.workspace.teamName}</Sheet.Description>{/if}
    </Sheet.Header>
    <div class="flex flex-col gap-5 px-4 pb-4">
      {#if status.workspace?.disconnected}
        <p class="text-muted-foreground">Slack no longer accepts ScreenHire. An owner or admin reconnects it in Settings.</p>
      {:else if !account}
        <p>Get your mentions and the tasks given to you as direct messages from ScreenHire.</p>
        <div><Button size="lg" disabled={linking} onclick={linkAccount}><HashIcon aria-hidden="true" />Link my Slack account</Button></div>
      {:else}
        {#if account.problem}<Notice>{slackProblems[account.problem]}</Notice>{/if}
        <p>Linked as <span class="font-semibold">{account.slackName}</span>.</p>
        <div class="flex items-start gap-4">
          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <Field.Label for="slack-mentions">Mentions</Field.Label>
            <Field.Description id="slack-mentions-hint">When someone mentions you in a note.</Field.Description>
          </div>
          <Switch
            id="slack-mentions"
            checked={account.mentions}
            onCheckedChange={(checked) => change({ mentions: checked, tasks: account.tasks })}
            aria-describedby="slack-mentions-hint"
            class="mt-1"
          />
        </div>
        <div class="flex items-start gap-4">
          <div class="flex min-w-0 flex-1 flex-col gap-1">
            <Field.Label for="slack-tasks">Tasks given to me</Field.Label>
            <Field.Description id="slack-tasks-hint">When someone gives you a task about a candidate.</Field.Description>
          </div>
          <Switch
            id="slack-tasks"
            checked={account.tasks}
            onCheckedChange={(checked) => change({ mentions: account.mentions, tasks: checked })}
            aria-describedby="slack-tasks-hint"
            class="mt-1"
          />
        </div>
        <div class="flex flex-wrap gap-2">
          {#if account.problem}
            <Button size="lg" disabled={linking} onclick={linkAccount}><HashIcon aria-hidden="true" />Link again</Button>
          {/if}
          <Button variant="outline" size="lg" disabled={unlink.isPending} onclick={unlinkAccount}><UnlinkIcon aria-hidden="true" />Unlink</Button>
        </div>
      {/if}
      <FailureText message={failure} />
      <p role="status" class="text-muted-foreground empty:hidden">{failure ? '' : saved}</p>
    </div>
  </Sheet.Content>
</Sheet.Root>
