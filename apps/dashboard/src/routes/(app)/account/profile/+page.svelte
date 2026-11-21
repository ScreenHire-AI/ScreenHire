<script lang="ts">
  import SaveIcon from '~icons/lucide/save';
  import type { DateFormat, HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createGetAccount,
    createRemovePhoto,
    createUpdateProfile,
    createUploadPhoto,
    getGetAccountQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { DropArea } from '@screenhire/ui/questions';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { invalidateAll } from '$app/navigation';
  import { accountCrumbs, dateFormats, languages, squarePhoto } from '#lib/account.js';
  import AccountMenu from '#lib/components/account-menu.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { configureDates } from '#lib/date-settings.svelte.js';
  import { initials } from '#lib/format.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import { timeZoneOptions } from '#lib/schedule-interview.js';

  // Your account > Profile (design system section 21), on the form template: the person's name and photo (the same in every
  // organization), the address they sign in with (read only: it is how ScreenHire knows them), and how they read dates here:
  // time zone, language and date format. The daily digest arrives at 08:00 in this time zone.
  const queryClient = useQueryClient();
  const account = createGetAccount();
  const details = $derived(account.data?.data);
  const settled = async () => {
    await queryClient.invalidateQueries({ queryKey: getGetAccountQueryKey(), exact: true });
    // The sidebar's name and photo come from the session the root layout loads.
    await invalidateAll();
  };
  const update = createUpdateProfile(() => ({ mutation: { onSuccess: settled } }));
  const upload = createUploadPhoto(() => ({ mutation: { onSuccess: settled } }));
  const removePhoto = createRemovePhoto(() => ({ mutation: { onSuccess: settled } }));

  const zones = timeZoneOptions();

  const schema = z.object({
    name: z.string().trim().min(1, 'Enter your name.').max(200, 'Keep your name to 200 characters or fewer.'),
    timeZone: z.string(),
    language: z.string(),
    dateFormat: z.enum(['Browser', 'DayMonthYear', 'MonthDayYear', 'Iso']),
  });

  let saved = $state('');
  const initial = { name: '', timeZone: '', language: '', dateFormat: 'Browser' as DateFormat };
  const { form, errors, enhance, submitting, reset } = superForm(defaults(initial, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      const data = submitted.data;
      try {
        const response = await update.mutateAsync({
          data: { name: data.name.trim(), timeZone: data.timeZone || null, language: data.language || null, dateFormat: data.dateFormat },
        });
        configureDates(response.data);
        saved = 'Saved.';
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save your profile')];
        }
      }
    },
  });

  // The form starts from the account once it loads; a refetch after that keeps what was typed.
  let started = false;
  $effect(() => {
    if (details && !started) {
      started = true;
      reset({ data: { name: details.name ?? '', timeZone: details.timeZone ?? '', language: details.language ?? '', dateFormat: details.dateFormat } });
    }
  });

  let photoProblem = $state('');
  let photoStatus = $state('');
  async function choosePhoto(file: File) {
    photoProblem = '';
    photoStatus = 'Uploading...';
    try {
      await upload.mutateAsync({ data: { data: await squarePhoto(file) } });
      photoStatus = 'Your photo is saved.';
    } catch (error) {
      photoStatus = '';
      photoProblem = describeError(error, 'Could not save your photo');
    }
  }

  async function dropPhoto() {
    photoProblem = '';
    try {
      await removePhoto.mutateAsync();
      photoStatus = 'Your photo is removed.';
    } catch (error) {
      photoProblem = describeError(error, 'Could not remove your photo');
    }
  }
</script>

<svelte:head>
  <title>Profile · ScreenHire</title>
</svelte:head>

<TopBar crumbs={accountCrumbs('/account/profile')} />

<main class="flex flex-col">
  <HeaderBand title="Profile" oneRow />

  <AccountMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      {#if account.isError && !details}
        <Panel title="You">
          <QueryError message={describeError(account.error, 'Could not load your profile')} onretry={() => account.refetch()} inset />
        </Panel>
      {:else if !details}
        <Panel title="You">
          <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-14 w-full" /><Skeleton class="h-14 w-full" /></div>
          <p class="sr-only" role="status">Loading your profile…</p>
        </Panel>
      {:else}
        <Panel title="Photo">
          <div class="flex items-center gap-4">
            {#if details.photoUrl}
              <img src={details.photoUrl} alt="" class="size-16 shrink-0 rounded-full object-cover" />
            {:else}
              <span aria-hidden="true" class="grid size-16 shrink-0 place-items-center rounded-full bg-secondary text-title font-semibold text-secondary-foreground"
                >{initials(details.displayName)}</span
              >
            {/if}
            <Field.Field data-invalid={photoProblem ? true : undefined} class="min-w-0 flex-1">
              <Field.Label for="account-photo" class="sr-only">Photo</Field.Label>
              <DropArea
                tone="app"
                id="account-photo"
                accept={['.png', '.jpg', '.jpeg', '.webp']}
                maxBytes={10 * 1024 * 1024}
                invalid={!!photoProblem}
                describedby="account-photo-hint"
                disabled={upload.isPending}
                status={upload.isPending ? 'Uploading...' : null}
                chooseLabel={details.photoUrl ? 'Choose another photo' : 'Choose a photo'}
                onfile={choosePhoto}
                onproblem={(message) => (photoProblem = message)}
              />
              <Field.Description id="account-photo-hint">PNG, JPEG or WebP. It is cropped to a square.</Field.Description>
              <Field.Error errors={photoProblem ? [{ message: photoProblem }] : []} />
            </Field.Field>
          </div>
          {#snippet footer()}
            <div class="flex flex-wrap items-center justify-end gap-2">
              <p role="status" class="mr-auto text-muted-foreground empty:hidden">{photoStatus}</p>
              {#if details.photoUrl}
                <Button variant="outline" size="lg" disabled={removePhoto.isPending} onclick={dropPhoto}>Remove photo</Button>
              {/if}
            </div>
          {/snippet}
        </Panel>

        <form method="POST" use:enhance novalidate aria-label="Your profile">
          <Panel title="You">
            <div class="flex flex-col gap-5">
              <Field.Field data-invalid={$errors.name ? true : undefined}>
                <Field.Label for="account-name">Name</Field.Label>
                <Input
                  id="account-name"
                  name="name"
                  maxlength={200}
                  required
                  autocomplete="name"
                  bind:value={$form.name}
                  aria-invalid={$errors.name ? 'true' : undefined}
                  aria-describedby={$errors.name ? 'account-name-error' : undefined}
                />
                <Field.Error id="account-name-error" errors={$errors.name?.map((message) => ({ message }))} />
              </Field.Field>
              <Field.Field>
                <Field.Label for="account-email">Email</Field.Label>
                <Input id="account-email" value={details.email} readonly aria-describedby="account-email-hint" />
                <Field.Description id="account-email-hint">You sign in with this address, so it cannot change here.</Field.Description>
              </Field.Field>
              <Field.Field data-invalid={$errors.timeZone ? true : undefined}>
                <Field.Label for="account-zone">Time zone</Field.Label>
                <select id="account-zone" name="timeZone" class={selectClass} bind:value={$form.timeZone} aria-describedby="account-zone-hint">
                  <option value="">This browser's time zone</option>
                  {#each zones as zone (zone.value)}<option value={zone.value}>{zone.label}</option>{/each}
                </select>
                <Field.Description id="account-zone-hint">Times show in it, and your daily digest arrives at 8:00 in it.</Field.Description>
                <Field.Error errors={$errors.timeZone?.map((message) => ({ message }))} />
              </Field.Field>
              <Field.Field data-invalid={$errors.language ? true : undefined}>
                <Field.Label for="account-language">Language</Field.Label>
                <select id="account-language" name="language" class={selectClass} bind:value={$form.language} aria-describedby="account-language-hint">
                  <option value="">This browser's language</option>
                  {#each languages as language (language.value)}<option value={language.value}>{language.label}</option>{/each}
                </select>
                <Field.Description id="account-language-hint">How dates and numbers are written.</Field.Description>
                <Field.Error errors={$errors.language?.map((message) => ({ message }))} />
              </Field.Field>
              <Field.Field>
                <Field.Label for="account-date-format">Date format</Field.Label>
                <select id="account-date-format" name="dateFormat" class={selectClass} bind:value={$form.dateFormat}>
                  {#each dateFormats as format (format.value)}
                    <option value={format.value}>{format.example ? `${format.label} (${format.example})` : format.label}</option>
                  {/each}
                </select>
              </Field.Field>
            </div>
            {#snippet footer()}
              <div class="flex flex-wrap items-center justify-end gap-2">
                <FailureText message={$errors._errors?.[0]} class="mr-auto" />
                <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
                <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
              </div>
            {/snippet}
          </Panel>
        </form>
      {/if}
    </div>
  </AccountMenu>
</main>
