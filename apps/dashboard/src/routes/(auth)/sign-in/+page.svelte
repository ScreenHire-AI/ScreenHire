<script lang="ts">
  import KeyRoundIcon from '~icons/lucide/key-round';
  import LogInIcon from '~icons/lucide/log-in';
  import { getChallengeProviderUrl, requestMagicLink } from '@screenhire/api-client';
  import { RequestMagicLinkBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { onMount, tick } from 'svelte';
  import { goto } from '$app/navigation';
  import FailureText from '#lib/components/failure-text.svelte';
  import GoogleMark from '#lib/components/provider-marks/google-mark.svelte';
  import MicrosoftMark from '#lib/components/provider-marks/microsoft-mark.svelte';
  import SignInCard from '#lib/components/sign-in-card.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { conditionalPasskeysSupported, passkeysSupported, runPasskeySignIn } from '#lib/passkeys.js';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  // Google and Microsoft carry their own marks, as their sign-in branding guidelines require (design system section 14).
  // The API reserves these two ids for them (ExternalProviders); any other provider, such as Dev SSO or an organization's
  // own OIDC, gets the generic sign-in icon. A Map, so an id like `constructor` finds nothing.
  const marks = new Map([
    ['google', GoogleMark],
    ['microsoft', MicrosoftMark],
  ]);

  // The address the link went to, once it has been sent.
  let sentTo = $state<string | undefined>();
  // The submit button disappears with the form, so focus moves to the page's new title (WCAG 2.4.3).
  let heading = $state<HTMLHeadingElement>();

  const { form, errors, enhance, submitting } = superForm(defaults(zod4(RequestMagicLinkBody)), {
    SPA: true,
    validators: zod4(RequestMagicLinkBody),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) {
        return;
      }

      // The link opens where this visit was going (an assistant's request to connect, say). The load already cut it
      // down to a local path; the API checks it again.
      const response = await requestMagicLink({ ...form.data, returnUrl: data.returnUrl });
      if (response.status === 202) {
        sentTo = form.data.email;
        await tick();
        heading?.focus();
      } else if (response.status === 400) {
        applyValidationProblem(form, response.data);
      } else {
        form.valid = false;
        form.errors._errors = ["We couldn't send a sign-in link just now. Try again in a minute."];
      }
    },
  });

  // Back to the form with the address still in it: the person fixes a typo or sends the same address another link
  // without typing it again.
  async function sendAnother() {
    sentTo = undefined;
    await tick();
    document.getElementById('email')?.focus();
  }

  // A passkey needs the browser's WebAuthn, so the button is a capability check, not a server setting. The server
  // cannot know whether this person has one; the browser's own prompt says so.
  const passkeys = passkeysSupported();
  let passkeyBusy = $state(false);
  let passkeyFailed = $state(false);
  // The quiet wait behind the email field, where a browser that can offers a saved passkey in the field's own list. It
  // ends when the person presses the passkey button instead, or leaves the page.
  let conditional: AbortController | undefined;

  async function finishPasskeySignIn(outcome: Awaited<ReturnType<typeof runPasskeySignIn>>) {
    if (outcome.kind === 'success') {
      // The session cookie is set; the guard runs again and finds the person, on to where this visit was going.
      await goto(data.returnUrl, { invalidateAll: true });
    } else if (outcome.kind === 'failed') {
      passkeyFailed = true;
    }
  }

  async function signInWithAPasskey() {
    conditional?.abort();
    passkeyBusy = true;
    passkeyFailed = false;
    const outcome = await runPasskeySignIn();
    passkeyBusy = false;
    await finishPasskeySignIn(outcome);
  }

  onMount(() => {
    if (!data.methods.magicLinkEnabled) return;
    const waiting = new AbortController();
    conditional = waiting;
    void conditionalPasskeysSupported().then(async (available) => {
      if (available && !waiting.signal.aborted) {
        await finishPasskeySignIn(await runPasskeySignIn({ signal: waiting.signal }));
      }
    });
    return () => waiting.abort();
  });

  const providers = $derived(data.methods.providers);
  const magicLink = $derived(data.methods.magicLinkEnabled);
  const anyMethod = $derived(providers.length > 0 || magicLink || passkeys);
  // The passkey button and the providers: the other ways in, below the email field.
  const others = $derived(providers.length > 0 || passkeys);

  // The documents the agreement line names: only the ones that have an address, each with the words that lead into it.
  const agreements = $derived(
    [
      { name: 'Terms of Service', href: data.methods.legal.termsUrl },
      { name: 'Privacy Policy', href: data.methods.legal.privacyUrl },
      { name: 'Data Processing Agreement', href: data.methods.legal.dpaUrl },
    ]
      .filter((document): document is { name: string; href: string } => Boolean(document.href))
      .map((document, index, all) => ({ ...document, lead: index === 0 ? ' ' : index === all.length - 1 ? ' and ' : ', ' })),
  );
</script>

<svelte:head>
  <title>Sign in · ScreenHire</title>
</svelte:head>

<!-- One centred card (design system section 7): the email field and Continue, an "or" divider, then the passkey and the
     providers, and the agreement line at the foot. -->
<SignInCard
  title={sentTo ? 'Check your email' : 'Sign in to ScreenHire'}
  description={!sentTo && anyMethod ? 'New here? Signing in creates your account.' : undefined}
  bind:heading
  footer={!sentTo && agreements.length > 0 ? agreement : undefined}
>
  {#if sentTo}
    <div class="flex flex-col gap-2" role="status">
      <p class="break-words">We sent a sign-in link to <strong class="font-semibold">{sentTo}</strong>. It works once, for the next 15 minutes.</p>
      <p class="text-muted-foreground">Nothing there? Check spam, or send another link.</p>
    </div>
    <Button variant="outline" size="lg" class="w-full" onclick={sendAnother}>Send another link</Button>
  {:else}
    <!-- Why a provider sign-in came back here, above the ways to try again. -->
    {#if data.errorMessage}
      <FailureText message={data.errorMessage} />
    {/if}

    <!-- Above the ways to try again, with a provider's own error. -->
    {#if passkeyFailed}
      <FailureText message="That passkey did not work. Use another way to sign in." />
    {/if}

    {#if magicLink}
      <form method="POST" use:enhance novalidate aria-label="Email link" class="flex flex-col gap-4">
        <Field.Field data-invalid={$errors.email ? true : undefined}>
          <Field.Label for="email">Work email</Field.Label>
          <!-- `webauthn` lets a browser offer a saved passkey in this field (conditional mediation) where it can. -->
          <Input
            id="email"
            name="email"
            type="email"
            autocomplete="email webauthn"
            required
            bind:value={$form.email}
            aria-invalid={$errors.email ? 'true' : undefined}
            aria-describedby={$errors.email ? 'email-error' : undefined}
          />
          <Field.Error id="email-error" errors={$errors.email?.map((message) => ({ message }))} />
        </Field.Field>
        <Button type="submit" size="lg" class="w-full" disabled={$submitting}>Continue</Button>
        {#if $errors._errors}
          <FailureText message={$errors._errors[0]} />
        {/if}
      </form>
    {/if}

    {#if magicLink && others}
      <div data-slot="or-divider" class="flex items-center gap-3 text-muted-foreground">
        <span aria-hidden="true" class="h-px flex-1 bg-border"></span>or<span aria-hidden="true" class="h-px flex-1 bg-border"></span>
      </div>
    {/if}

    {#if others}
      <ul class="flex flex-col gap-2">
        {#if passkeys}
          <li>
            <Button variant="outline" size="lg" class="w-full" disabled={passkeyBusy} onclick={signInWithAPasskey}>
              <KeyRoundIcon aria-hidden="true" />Sign in with a passkey
            </Button>
          </li>
        {/if}
        {#each providers as provider (provider.id)}
          {@const Mark = marks.get(provider.id)}
          <li>
            <!-- A full page load: the API answers with a redirect to the provider, which SvelteKit's router cannot follow. -->
            <Button
              href={getChallengeProviderUrl(provider.id, { returnUrl: data.returnUrl })}
              data-sveltekit-reload
              variant="outline"
              size="lg"
              class="w-full"
            >
              {#if Mark}<Mark />{:else}<LogInIcon aria-hidden="true" />{/if}Continue with {provider.displayName}
            </Button>
          </li>
        {/each}
      </ul>
    {/if}

    {#if !anyMethod}
      <FailureText message="Sign-in is not set up on this server yet." />
    {/if}
  {/if}
</SignInCard>

<!-- "By continuing, you agree to ScreenHire's Terms of Service, Privacy Policy and Data Processing Agreement.", naming only the documents with an address. -->
{#snippet agreement()}
  By continuing, you agree to ScreenHire's{#each agreements as document (document.name)}{document.lead}<a
      href={document.href}
      target="_blank"
      rel="noopener noreferrer"
      class="underline underline-offset-2 hover:text-foreground">{document.name}<span class="sr-only">{' '}(opens in a new tab)</span></a
    >{/each}.
{/snippet}
