<script lang="ts">
  import { getChallengeProviderUrl, requestMagicLink } from '@screenhire/api-client';
  import { RequestMagicLinkBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { tick } from 'svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  // The address the link went to, once it has been sent.
  let sentTo = $state<string | undefined>();
  // The submit button disappears with the form, so focus moves to the confirmation's heading (WCAG 2.4.3).
  let sentHeading = $state<HTMLHeadingElement>();

  const { form, errors, enhance, submitting, reset } = superForm(defaults(zod4(RequestMagicLinkBody)), {
    SPA: true,
    validators: zod4(RequestMagicLinkBody),
    resetForm: false,
    async onUpdate({ form }) {
      if (!form.valid) {
        return;
      }

      const response = await requestMagicLink(form.data);
      if (response.status === 202) {
        sentTo = form.data.email;
        await tick();
        sentHeading?.focus();
      } else if (response.status === 400) {
        applyValidationProblem(form, response.data);
      } else {
        form.valid = false;
        form.errors._errors = ["We couldn't send a sign-in link just now. Try again in a minute."];
      }
    },
  });

  function useAnotherAddress() {
    sentTo = undefined;
    reset();
  }
</script>

<svelte:head>
  <title>Sign in · ScreenHire</title>
</svelte:head>

{#if sentTo}
  <div class="flex flex-col gap-3" role="status">
    <h1 bind:this={sentHeading} tabindex="-1" class="font-display text-page-title">Check your email</h1>
    <p>
      We sent a sign-in link to <strong class="font-semibold">{sentTo}</strong>. It works once, for the next 15 minutes.
    </p>
    <p class="text-muted-foreground">Nothing there? Check spam, or ask for a new link.</p>
    <Button variant="outline" onclick={useAnotherAddress}>Use a different email</Button>
  </div>
{:else}
  <div class="flex flex-col gap-5">
    <div class="flex flex-col gap-1.5">
      <h1 class="font-display text-page-title">Sign in to ScreenHire</h1>
      <p class="text-muted-foreground">No password needed.</p>
    </div>

    {#if data.errorMessage}
      <p role="alert" class="rounded-md border border-border bg-secondary px-3 py-2">{data.errorMessage}</p>
    {/if}

    {#if data.methods.providers.length > 0}
      <ul class="flex flex-col gap-2">
        {#each data.methods.providers as provider (provider.id)}
          <li>
            <!-- A full page load: the API answers with a redirect to the provider, which SvelteKit's router cannot follow. -->
            <Button
              href={getChallengeProviderUrl(provider.id, { returnUrl: data.returnUrl })}
              data-sveltekit-reload
              variant="outline"
              size="lg"
              class="w-full"
            >
              Continue with {provider.displayName}
            </Button>
          </li>
        {/each}
      </ul>
    {/if}

    {#if data.methods.magicLinkEnabled}
      {#if data.methods.providers.length > 0}
        <div class="flex items-center gap-3 text-muted-foreground" aria-hidden="true">
          <span class="h-px flex-1 bg-border"></span>
          <span class="text-caption">or</span>
          <span class="h-px flex-1 bg-border"></span>
        </div>
      {/if}

      <form method="POST" use:enhance novalidate class="flex flex-col gap-3">
        <Field.Field data-invalid={$errors.email ? true : undefined}>
          <Field.Label for="email">Work email</Field.Label>
          <Input
            id="email"
            name="email"
            type="email"
            autocomplete="email"
            required
            bind:value={$form.email}
            aria-invalid={$errors.email ? 'true' : undefined}
            aria-describedby={$errors.email ? 'email-error' : undefined}
          />
          <Field.Error id="email-error" errors={$errors.email?.map((message) => ({ message }))} />
        </Field.Field>
        {#if $errors._errors}
          <p role="alert" class="text-body text-destructive">{$errors._errors[0]}</p>
        {/if}
        <Button type="submit" size="lg" disabled={$submitting}>Email me a sign-in link</Button>
      </form>
    {/if}

    {#if data.methods.providers.length === 0 && !data.methods.magicLinkEnabled}
      <p role="alert">Sign-in is not set up on this server yet.</p>
    {/if}
  </div>
{/if}
