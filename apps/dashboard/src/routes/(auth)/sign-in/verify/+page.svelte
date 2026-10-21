<script lang="ts">
  import { verifyMagicLink } from '@screenhire/api-client';
  import { tick } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The emailed link carries the token in the fragment, which the browser never sends to a server. The person
  // confirms with a click, so a mail scanner that opens the link cannot use it up.
  const token = $derived(new URLSearchParams(page.url.hash.slice(1)).get('token'));

  let failed = $state(false);
  let submitting = $state(false);
  // The Sign in button disappears when the link fails, so focus moves to the page's new title (WCAG 2.4.3).
  let heading = $state<HTMLHeadingElement>();
  const broken = $derived(!token || failed);

  async function signIn(event: SubmitEvent) {
    event.preventDefault();
    if (!token) {
      return;
    }

    submitting = true;
    const response = await verifyMagicLink({ token });
    submitting = false;
    if (response.status === 204) {
      await goto('/', { invalidateAll: true, replaceState: true });
    } else {
      failed = true;
      await tick();
      heading?.focus();
    }
  }
</script>

<svelte:head>
  <title>Finish signing in · ScreenHire</title>
</svelte:head>

<main class="flex flex-col">
  <HeaderBand form title={broken ? "This link doesn't work" : 'Finish signing in'} bind:heading />

  <!-- The form template (section 7): one 688px column, actions at the end. -->
  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    {#if broken}
      <Panel title="Email link" footer={newLink}>
        <p role="alert">This sign-in link has expired or has already been used. Each link works once, for 15 minutes.</p>
      </Panel>
    {:else}
      <form onsubmit={signIn} aria-label="Finish signing in">
        <Panel title="Email link" footer={confirm}>
          <p>You opened a sign-in link for ScreenHire.</p>
        </Panel>
      </form>
    {/if}
  </div>
</main>

{#snippet confirm()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <Button type="submit" size="lg" disabled={submitting}>Sign in</Button>
  </div>
{/snippet}

{#snippet newLink()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <Button href="/sign-in" size="lg">Get a new link</Button>
  </div>
{/snippet}
