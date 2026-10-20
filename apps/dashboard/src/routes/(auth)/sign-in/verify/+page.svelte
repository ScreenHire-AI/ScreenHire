<script lang="ts">
  import { verifyMagicLink } from '@screenhire/api-client';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { Button } from '#lib/components/ui/button/index.js';

  // The emailed link carries the token in the fragment, which the browser never sends to a server. The person
  // confirms with a click, so a mail scanner that opens the link cannot use it up.
  const token = $derived(new URLSearchParams(page.url.hash.slice(1)).get('token'));

  let failed = $state(false);
  let submitting = $state(false);

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
    }
  }
</script>

<svelte:head>
  <title>Finish signing in · ScreenHire</title>
</svelte:head>

{#if !token || failed}
  <div class="flex flex-col gap-3">
    <h1 class="font-display text-[22px] leading-tight font-bold">This link doesn't work</h1>
    <p role="alert">This sign-in link has expired or has already been used. Each link works once, for 15 minutes.</p>
    <Button href="/sign-in" size="lg">Get a new link</Button>
  </div>
{:else}
  <form class="flex flex-col gap-3" onsubmit={signIn}>
    <h1 class="font-display text-[22px] leading-tight font-bold">Finish signing in</h1>
    <p class="text-muted-foreground">You opened a sign-in link for ScreenHire.</p>
    <Button type="submit" size="lg" disabled={submitting}>Sign in</Button>
  </form>
{/if}
