<script lang="ts">
  import { completeOnboarding } from '@screenhire/api-client';
  import { CompleteOnboardingBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import LogOutIcon from '~icons/lucide/log-out';
  import MailIcon from '~icons/lucide/mail';
  import PlusIcon from '~icons/lucide/plus';
  import { goto } from '$app/navigation';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { applyValidationProblem, slugify } from '#lib/forms.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  // The address follows the name until the person edits it themselves.
  let slugEdited = $state(false);

  const { form, errors, enhance, submitting } = superForm(defaults(zod4(CompleteOnboardingBody)), {
    SPA: true,
    validators: zod4(CompleteOnboardingBody),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) {
        return;
      }

      const response = await completeOnboarding(form.data);
      if (response.status === 200 || response.status === 409) {
        // 409: this person already has an organization, so there is nothing to set up.
        await goto('/', { invalidateAll: true });
      } else if (response.status === 400) {
        applyValidationProblem(form, response.data);
      } else if (response.status === 401) {
        await goto('/sign-in', { invalidateAll: true });
      } else {
        form.valid = false;
        form.errors._errors = ["We couldn't create your organization just now. Try again in a minute."];
      }
    },
  });

  function nameChanged() {
    if (!slugEdited) {
      $form.slug = slugify($form.organizationName);
    }
  }
</script>

<svelte:head>
  <title>Set up your organization · ScreenHire</title>
</svelte:head>

<main class="flex flex-col">
  <HeaderBand form title="Set up your organization">
    {#snippet details()}
      {#if data.user?.email}
        <span class="flex min-w-0 items-center gap-1.5">
          <MailIcon aria-hidden="true" class="shrink-0" />
          <span class="sr-only">Signed in as</span>
          <TruncatedText text={data.user.email} />
        </span>
      {/if}
    {/snippet}
    {#snippet actions()}
      <Button variant="outline" size="lg" onclick={signOutAndRedirect}><LogOutIcon aria-hidden="true" />Sign out</Button>
    {/snippet}
  </HeaderBand>

  <!-- The form template (section 7): one 688px column, labels above fields, actions at the end. -->
  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    <form method="POST" use:enhance novalidate aria-label="Set up your organization">
      <Panel title="Organization" footer={create}>
        <div class="flex flex-col gap-4">
          <Field.Field data-invalid={$errors.organizationName ? true : undefined}>
            <Field.Label for="organizationName">Organization name</Field.Label>
            <Input
              id="organizationName"
              name="organizationName"
              autocomplete="organization"
              required
              bind:value={$form.organizationName}
              oninput={nameChanged}
              aria-invalid={$errors.organizationName ? 'true' : undefined}
              aria-describedby={$errors.organizationName ? 'organizationName-error' : undefined}
            />
            <Field.Error id="organizationName-error" errors={$errors.organizationName?.map((message) => ({ message }))} />
          </Field.Field>

          <Field.Field data-invalid={$errors.slug ? true : undefined}>
            <Field.Label for="slug">Careers page address</Field.Label>
            <Input
              id="slug"
              name="slug"
              required
              autocapitalize="none"
              spellcheck={false}
              class="font-mono"
              bind:value={$form.slug}
              oninput={() => (slugEdited = true)}
              aria-invalid={$errors.slug ? 'true' : undefined}
              aria-describedby={$errors.slug ? 'slug-hint slug-error' : 'slug-hint'}
            />
            <Field.Description id="slug-hint">Lower-case letters, numbers and hyphens.</Field.Description>
            <Field.Error id="slug-error" errors={$errors.slug?.map((message) => ({ message }))} />
          </Field.Field>
        </div>
      </Panel>
    </form>
  </div>
</main>

{#snippet create()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    {#if $errors._errors}
      <FailureText message={$errors._errors[0]} class="mr-auto" />
    {/if}
    <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Create organization</Button>
  </div>
{/snippet}
