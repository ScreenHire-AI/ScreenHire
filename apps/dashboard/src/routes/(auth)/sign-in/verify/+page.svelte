<script lang="ts">
  import LogInIcon from '~icons/lucide/log-in';
  import MailIcon from '~icons/lucide/mail';
  import { verifyMagicLink } from '@screenhire/api-client';
  import { tick } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { returnUrlParameter, safeReturnUrl } from '#lib/auth.js';
  import FailureText from '#lib/components/failure-text.svelte';
  import SignInCard from '#lib/components/sign-in-card.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The emailed link carries the token in the fragment, which the browser never sends to a server. The person
  // confirms with a click, so a mail scanner that opens the link cannot use it up.
  const fragment = $derived(new URLSearchParams(page.url.hash.slice(1)));
  const token = $derived(fragment.get('token'));
  // Where the person was going when they asked for the link, beside the token. Cut down to a local path here as well.
  const returnUrl = $derived(safeReturnUrl(fragment.get(returnUrlParameter)));

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
      await goto(returnUrl, { invalidateAll: true, replaceState: true });
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

<!-- The same card as the sign-in page (design system section 7). -->
<SignInCard
  title={broken ? "This link doesn't work" : 'Finish signing in'}
  description={broken ? undefined : 'This link signs you in on this device.'}
  bind:heading
>
  {#if broken}
    <FailureText message="This sign-in link has expired or has already been used. Each link works once, for 15 minutes." />
    <Button href="/sign-in" size="lg" class="w-full"><MailIcon aria-hidden="true" />Get a new link</Button>
  {:else}
    <form onsubmit={signIn} aria-label="Finish signing in">
      <Button type="submit" size="lg" class="w-full" disabled={submitting}><LogInIcon aria-hidden="true" />Sign in</Button>
    </form>
  {/if}
</SignInCard>
