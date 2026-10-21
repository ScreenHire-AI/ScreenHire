<script lang="ts">
  import { getChallengeProviderUrl, requestMagicLink } from '@screenhire/api-client';
  import { RequestMagicLinkBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { tick } from 'svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  // The address the link went to, once it has been sent.
  let sentTo = $state<string | undefined>();
  // The submit button disappears with the form, so focus moves to the page's new title (WCAG 2.4.3).
  let heading = $state<HTMLHeadingElement>();

  const { form, errors, enhance, submitting, reset } = superForm(defaults(zod4(RequestMagicLinkBody)), {
    SPA: true,
    validators: zod4(RequestMagicLinkBody),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) {
        return;
      }

      const response = await requestMagicLink(form.data);
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

  async function useAnotherAddress() {
    sentTo = undefined;
    reset();
    await tick();
    document.getElementById('email')?.focus();
  }

  const providers = $derived(data.methods.providers);
  const magicLink = $derived(data.methods.magicLinkEnabled);
</script>

<svelte:head>
  <title>Sign in · ScreenHire</title>
</svelte:head>

<main class="flex flex-col">
  <HeaderBand title={sentTo ? 'Check your email' : 'Sign in'} bind:heading />

  <!-- The form template (section 7): one 688px column, labels above fields, actions at the end. -->
  <div class="mx-auto flex w-full max-w-form flex-col gap-5 px-4 py-6 md:px-6">
    {#if sentTo}
      <Panel title="Email link" footer={anotherAddress}>
        <div class="flex flex-col gap-2" role="status">
          <p>We sent a sign-in link to <strong class="font-semibold">{sentTo}</strong>. It works once, for the next 15 minutes.</p>
          <p class="text-muted-foreground">Nothing there? Check spam, or ask for a new link.</p>
        </div>
      </Panel>
    {:else}
      {#if providers.length > 0}
        <Panel title="Single sign-on">
          <div class="flex flex-col gap-3">
            {@render providerError()}
            <ul class="flex flex-col gap-2">
              {#each providers as provider (provider.id)}
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
          </div>
        </Panel>
      {/if}

      {#if magicLink}
        <form method="POST" use:enhance novalidate aria-label="Email link">
          <Panel title="Email link" footer={sendLink}>
            <div class="flex flex-col gap-3">
              {#if providers.length === 0}{@render providerError()}{/if}
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
            </div>
          </Panel>
        </form>
      {/if}

      {#if providers.length === 0 && !magicLink}
        <Panel title="Sign-in methods">
          <p role="alert">Sign-in is not set up on this server yet.</p>
        </Panel>
      {/if}
    {/if}
  </div>
</main>

<!-- Why a provider sign-in came back here, in the first panel. -->
{#snippet providerError()}
  {#if data.errorMessage}
    <p role="alert" class="text-body text-destructive">{data.errorMessage}</p>
  {/if}
{/snippet}

{#snippet sendLink()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    {#if $errors._errors}
      <p role="alert" class="mr-auto text-body text-destructive">{$errors._errors[0]}</p>
    {/if}
    <Button type="submit" size="lg" disabled={$submitting}>Email me a sign-in link</Button>
  </div>
{/snippet}

{#snippet anotherAddress()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <Button variant="outline" size="lg" onclick={useAnotherAddress}>Use a different email</Button>
  </div>
{/snippet}
