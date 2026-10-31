<script lang="ts">
  import SaveIcon from '@lucide/svelte/icons/save';
  import type { HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createGetOrganizationTalentPoolSettings,
    createUpdateOrganizationTalentPoolSettings,
    getGetOrganizationTalentPoolSettingsHistoryQueryOptions,
    getGetOrganizationTalentPoolSettingsQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { talentPoolPath } from '#lib/realtime.js';

  // The organization's talent pool settings (docs/architecture/talent-pool.md): how long a candidate's permission to keep
  // their details for future roles lasts, 12 months unless an owner or admin changes it here, from 6 to 24. A shorter
  // period shortens every permission at once; a longer one never extends a permission already given.
  const queryClient = useQueryClient();
  const settings = createGetOrganizationTalentPoolSettings();
  const detail = $derived(settings.data?.data);
  const update = createUpdateOrganizationTalentPoolSettings(() => ({
    mutation: {
      onSuccess: () =>
        Promise.all([
          queryClient.invalidateQueries({ queryKey: getGetOrganizationTalentPoolSettingsQueryKey(), exact: true }),
          invalidatePath(queryClient, talentPoolPath),
        ]),
    },
  }));

  const min = $derived(detail?.minMonths ?? 6);
  const max = $derived(detail?.maxMonths ?? 24);
  const schema = z.object({
    permissionMonths: z
      .string()
      .trim()
      .regex(/^\d+$/, 'Enter a whole number of months.'),
  });

  let saved = $state('');
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ permissionMonths: '12' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      const months = Number(submitted.data.permissionMonths);
      if (months < min || months > max) {
        submitted.valid = false;
        submitted.errors.permissionMonths = [`Enter a number from ${min} to ${max}.`];
        return;
      }

      try {
        await update.mutateAsync({ data: { permissionMonths: months } });
        saved = 'Saved.';
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the settings')];
        }
      }
    },
  });

  // The form starts from the saved settings once they load; a refetch after that keeps what was changed.
  let started = false;
  $effect(() => {
    if (detail && !started) {
      started = true;
      reset({ data: { permissionMonths: detail.permissionMonths.toString() } });
    }
  });
</script>

<svelte:head>
  <title>Talent pool · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Talent pool' }]} />

<main class="flex flex-col">
  <HeaderBand title="Talent pool">
    {#snippet actions()}
      {#if detail}
        <HistoryFact value={detail.lastUpdated} name="Talent pool settings" page={(before) => getGetOrganizationTalentPoolSettingsHistoryQueryOptions({ before })} />
      {/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
    {#if settings.isError && !detail}
      <Panel title="Permission">
        <QueryError message={describeError(settings.error, 'Could not load the talent pool settings')} onretry={() => settings.refetch()} inset />
      </Panel>
    {:else if !detail}
      <Panel title="Permission">
        <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-14 w-full" /></div>
        <p class="sr-only" role="status">Loading the talent pool settings…</p>
      </Panel>
    {:else}
      <form method="POST" use:enhance novalidate aria-label="Permission">
        <Panel title="Permission">
          <Field.Field data-invalid={$errors.permissionMonths ? true : undefined}>
            <Field.Label for="permission-months">Permission lasts</Field.Label>
            <div class="flex items-center gap-2">
              <Input
                id="permission-months"
                name="permissionMonths"
                inputmode="numeric"
                autocomplete="off"
                class="w-24 tabular-nums"
                bind:value={$form.permissionMonths}
                aria-invalid={$errors.permissionMonths ? 'true' : undefined}
                aria-describedby={['permission-months-hint', $errors.permissionMonths && 'permission-months-error'].filter(Boolean).join(' ')}
              />
              <span class="text-muted-foreground">months</span>
            </div>
            <Field.Description id="permission-months-hint">
              From when a candidate ticks "Keep my details for future roles", {min} to {max} months. Then they leave the pool.
            </Field.Description>
            <Field.Error id="permission-months-error" errors={$errors.permissionMonths?.map((message) => ({ message }))} />
          </Field.Field>
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
  </SettingsMenu>
</main>
