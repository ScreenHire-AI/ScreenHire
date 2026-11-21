<script lang="ts">
  import KeyRoundIcon from '~icons/lucide/key-round';
  import LogOutIcon from '~icons/lucide/log-out';
  import { createGetAccountSignIn, createSignOutEverywhere } from '@screenhire/api-client/svelte-query';
  import { accountCrumbs } from '#lib/account.js';
  import AccountMenu from '#lib/components/account-menu.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';

  // Your account > Sign-in and security (docs/architecture/sign-in-and-tenancy.md, "Your account"): the ways the person signs
  // in, their passkeys (a page of their own, under this entry), and Sign out everywhere, which ends every other session within
  // a minute and keeps this one. ScreenHire keeps no list of sessions (the session is a cookie), so there is no list here.
  const signIn = createGetAccountSignIn();
  const methods = $derived(signIn.data?.data);
  const signOutEverywhere = createSignOutEverywhere();
  let confirming = $state(false);
  let done = $state('');
</script>

<svelte:head>
  <title>Sign-in and security · ScreenHire</title>
</svelte:head>

<TopBar crumbs={accountCrumbs('/account/security')} />

<main class="flex flex-col">
  <HeaderBand title="Sign-in and security" oneRow />

  <AccountMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      {#if signIn.isError && !methods}
        <Panel title="How you sign in">
          <QueryError message={describeError(signIn.error, 'Could not load how you sign in')} onretry={() => signIn.refetch()} inset />
        </Panel>
      {:else if !methods}
        <Panel title="How you sign in">
          <div aria-hidden="true" class="flex flex-col gap-3"><Skeleton class="h-5 w-full" /><Skeleton class="h-5 w-full" /></div>
          <p class="sr-only" role="status">Loading how you sign in…</p>
        </Panel>
      {:else}
        <Panel title="How you sign in">
          <SummaryList>
            {#if methods.emailLinks}<SummaryItem label="Email link">{methods.email}</SummaryItem>{/if}
            {#each methods.providers as provider (provider.id)}<SummaryItem label={provider.name}>Signed in before</SummaryItem>{/each}
            <SummaryItem label="Passkeys">{methods.passkeys === 0 ? 'None yet' : methods.passkeys === 1 ? '1 passkey' : `${methods.passkeys} passkeys`}</SummaryItem>
          </SummaryList>
          {#snippet footer()}
            <div class="flex justify-end">
              <a href="/account/passkeys" class={buttonVariants({ variant: 'outline', size: 'lg' })}><KeyRoundIcon aria-hidden="true" />Manage passkeys</a>
            </div>
          {/snippet}
        </Panel>
      {/if}

      <Panel title="Sessions">
        <p>Signed in on another computer or phone you no longer use? Sign out everywhere ends every other session within a minute. You stay signed in here.</p>
        {#snippet footer()}
          <div class="flex flex-wrap items-center justify-end gap-2">
            <p role="status" class="mr-auto text-muted-foreground empty:hidden">{done}</p>
            <Button variant="outline" size="lg" onclick={() => (confirming = true)}><LogOutIcon aria-hidden="true" />Sign out everywhere</Button>
          </div>
        {/snippet}
      </Panel>
    </div>
  </AccountMenu>
</main>

<ConfirmDialog
  bind:open={confirming}
  title="Sign out everywhere else?"
  description="Every other browser and device signed in as you is signed out within a minute. Connected AI assistants keep their access until you disconnect them."
  confirmLabel="Sign out everywhere"
  failure="Could not sign out everywhere"
  onconfirm={async () => {
    await signOutEverywhere.mutateAsync();
    done = 'Every other session is ending.';
  }}
>
  {#snippet icon()}<LogOutIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>
