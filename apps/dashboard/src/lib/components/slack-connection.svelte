<script lang="ts">
  import HashIcon from '~icons/lucide/hash';
  import UnlinkIcon from '~icons/lucide/unlink';
  import type { SlackStatus } from '@screenhire/api-client';
  import { createStartSlackLink, createUnlinkSlackAccount } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { untrack } from 'svelte';
  import { page } from '$app/state';
  import FailureText from '#lib/components/failure-text.svelte';
  import Notice from '#lib/components/notice.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { slackProblems } from '#lib/slack.js';

  // A person's own Slack account (docs/architecture/slack.md), a section of Your account > Connected accounts: link it with
  // Sign in with Slack, or unlink it. Which direct messages they get is the Slack column of their notification grid. Every
  // member has it while the organization's workspace is connected; managing the workspace is Settings > Slack.
  let {
    status,
    outcome = '',
  }: {
    status: SlackStatus;
    /** What the last round trip to Slack said, when it failed. */
    outcome?: string;
  } = $props();

  const queryClient = useQueryClient();
  const settled = () => {
    void invalidatePath(queryClient, '/v1/slack');
    // The grid's Slack column shows once an account is linked.
    void invalidatePath(queryClient, '/v1/account');
  };
  const link = createStartSlackLink();
  const unlink = createUnlinkSlackAccount(() => ({ mutation: { onSettled: settled } }));

  const account = $derived(status.account);
  let failure = $state(untrack(() => outcome));
  let saved = $state('');
  let linking = $state(false);

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

<div class="flex flex-col gap-5">
  {#if status.workspace?.disconnected}
    <p class="text-muted-foreground">Slack no longer accepts ScreenHire. An owner or admin reconnects it in Settings.</p>
  {:else if !account}
    <p>Get your mentions, the tasks given to you and the other messages you choose in Notifications as direct messages from ScreenHire.</p>
    <div><Button size="lg" disabled={linking} onclick={linkAccount}><HashIcon aria-hidden="true" />Link my Slack account</Button></div>
  {:else}
    {#if account.problem}<Notice>{slackProblems[account.problem]}</Notice>{/if}
    <p>Linked as <span class="font-semibold">{account.slackName}</span>. Choose which messages you get in <a href="/account/notifications">Notifications</a>.</p>
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
