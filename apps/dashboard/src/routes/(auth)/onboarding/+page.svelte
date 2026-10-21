<script lang="ts">
  import { completeOnboarding } from '@screenhire/api-client';
  import { CompleteOnboardingBody } from '@screenhire/api-client/zod';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { goto } from '$app/navigation';
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

<div class="flex flex-col gap-5">
  <div class="flex flex-col gap-1.5">
    <h1 class="font-display text-page-title">Set up your organization</h1>
    <p class="text-muted-foreground">You'll be its owner, and you can invite your team later.</p>
  </div>

  <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
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
      <Field.Description id="slug-hint">Lower-case letters, numbers and hyphens. Candidates see it in your careers page links.</Field.Description>
      <Field.Error id="slug-error" errors={$errors.slug?.map((message) => ({ message }))} />
    </Field.Field>

    {#if $errors._errors}
      <p role="alert" class="text-body text-destructive">{$errors._errors[0]}</p>
    {/if}
    <Button type="submit" size="lg" disabled={$submitting}>Create organization</Button>
  </form>

  <p class="text-body text-muted-foreground">
    Signed in as {data.user?.email}.
    <button type="button" class="font-semibold text-primary underline-offset-4 hover:underline" onclick={signOutAndRedirect}>Sign out</button>
  </p>
</div>
