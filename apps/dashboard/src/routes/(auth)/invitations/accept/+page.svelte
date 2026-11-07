<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import UserPlusIcon from '~icons/lucide/user-plus';
  import { acceptInvitation, lookUpInvitation, type InvitationDetails } from '@screenhire/api-client';
  import { tick } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';

  // As on the sign-in link page: the token is in the fragment, which the browser never sends to a server, and the
  // person confirms with a click, so a mail scanner that opens the link cannot use the invitation up.
  const token = $derived(new URLSearchParams(page.url.hash.slice(1)).get('invitation'));

  let failed = $state(false);
  // Which organization and role accepting joins, asked for when the page opens. Looking it up uses nothing up. If the
  // answer never comes (the network), the page still offers Accept, which has its own refusal; only a refusal here
  // shows the broken page early.
  let details = $state<InvitationDetails>();
  let looked = $state(false);

  $effect(() => {
    const current = token;
    if (!current) {
      return;
    }

    let stale = false;
    lookUpInvitation({ token: current })
      .then((response) => {
        if (stale) return;
        if (response.status === 200) {
          details = response.data;
        } else if (response.status === 400) {
          failed = true;
        }
      })
      .catch(() => {})
      .finally(() => {
        if (!stale) looked = true;
      });
    return () => {
      stale = true;
    };
  });
  let submitting = $state(false);
  // The Accept button disappears when the invitation fails, so focus moves to the page's new title (WCAG 2.4.3).
  let heading = $state<HTMLHeadingElement>();
  const broken = $derived(!token || failed);

  async function accept(event: SubmitEvent) {
    event.preventDefault();
    if (!token) {
      return;
    }

    submitting = true;
    const response = await acceptInvitation({ token });
    submitting = false;
    if (response.status === 200) {
      await goto('/', { invalidateAll: true, replaceState: true });
    } else {
      failed = true;
      await tick();
      heading?.focus();
    }
  }
</script>

<svelte:head>
  <title>Join your team · ScreenHire</title>
</svelte:head>

<main class="flex flex-col">
  <HeaderBand form title={broken ? "This invitation doesn't work" : 'Join your team'} bind:heading />

  <!-- The form template (section 7): one 688px column, actions at the end. -->
  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    {#if broken}
      <Panel title="Invitation" footer={home}>
        <FailureText message="This invitation has expired or is no longer valid. Ask whoever invited you for a new one." />
      </Panel>
    {:else}
      <form onsubmit={accept} aria-label="Join your team">
        <Panel title="Invitation" footer={confirm}>
          {#if details}
            <p>
              You're joining {details.organizationName} as {details.roleName}. This signs you in on this device with the address the invitation was sent to.
            </p>
          {:else if !looked}
            <Skeleton class="h-5 w-full max-w-measure" />
            <span class="sr-only">Checking the invitation</span>
          {:else}
            <p>This signs you in on this device with the address the invitation was sent to.</p>
          {/if}
        </Panel>
      </form>
    {/if}
  </div>
</main>

{#snippet confirm()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <Button type="submit" size="lg" disabled={submitting}><UserPlusIcon aria-hidden="true" />Accept invitation</Button>
  </div>
{/snippet}

{#snippet home()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <Button href="/" size="lg"><ArrowRightIcon aria-hidden="true" />Go to ScreenHire</Button>
  </div>
{/snippet}
