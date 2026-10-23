<script lang="ts">
  import SaveIcon from '@lucide/svelte/icons/save';
  import type { HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createGetOrganizationIntegritySettings,
    createUpdateOrganizationIntegritySettings,
    getGetOrganizationIntegritySettingsQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { locales } from '#lib/dates.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';

  // The organization's integrity settings (integrity-engine.md section 10): the LLM consistency check, off until an owner
  // or admin turns it on here, and its monthly token budget. The other checks need no switch.
  const queryClient = useQueryClient();
  const settings = createGetOrganizationIntegritySettings();
  const detail = $derived(settings.data?.data);
  const update = createUpdateOrganizationIntegritySettings(() => ({
    mutation: { onSuccess: () => queryClient.invalidateQueries({ queryKey: getGetOrganizationIntegritySettingsQueryKey(), exact: true }) },
  }));

  const schema = z.object({
    llmConsistencyCheck: z.boolean(),
    monthlyTokenBudget: z
      .string()
      .trim()
      .regex(/^\d*$/, 'Enter a whole number of tokens, or leave it empty.'),
  });

  let saved = $state('');
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ llmConsistencyCheck: false, monthlyTokenBudget: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      const budget = submitted.data.monthlyTokenBudget.trim();
      try {
        await update.mutateAsync({
          data: { llmConsistencyCheck: submitted.data.llmConsistencyCheck, monthlyTokenBudget: budget ? Number(budget) : null },
        });
        saved = 'Saved. It applies from the next application.';
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
      reset({ data: { llmConsistencyCheck: detail.llmConsistencyCheck, monthlyTokenBudget: detail.monthlyTokenBudget?.toString() ?? '' } });
    }
  });

  const tokens = new Intl.NumberFormat(locales);
</script>

<svelte:head>
  <title>Integrity · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Integrity' }]} />

<main class="flex flex-col">
  <HeaderBand form title="Integrity" />

  <div class="mx-auto flex w-full max-w-form flex-col gap-5 px-4 py-6 md:px-6">
    {#if settings.isError && !detail}
      <Panel title="Consistency check">
        <QueryError message={describeError(settings.error, 'Could not load the integrity settings')} onretry={() => settings.refetch()} inset />
      </Panel>
    {:else if !detail}
      <Panel title="Consistency check">
        <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-10 w-full" /><Skeleton class="h-14 w-full" /></div>
        <p class="sr-only" role="status">Loading the integrity settings…</p>
      </Panel>
    {:else}
      <form method="POST" use:enhance novalidate aria-label="Consistency check">
        <Panel title="Consistency check">
          <div class="flex flex-col gap-5">
            <div class="flex items-start gap-4">
              <div class="flex min-w-0 flex-1 flex-col gap-1">
                <Field.Label for="consistency-check">Compare each application's sources</Field.Label>
                <Field.Description id="consistency-check-hint">Sends the resume, answers and any uploaded LinkedIn export to ScreenHire's AI provider.</Field.Description>
              </div>
              <Switch id="consistency-check" bind:checked={$form.llmConsistencyCheck} aria-describedby="consistency-check-hint" class="mt-1" />
            </div>

            <Field.Field data-invalid={$errors.monthlyTokenBudget ? true : undefined}>
              <Field.Label for="token-budget">Monthly token budget <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
              <Input
                id="token-budget"
                name="monthlyTokenBudget"
                inputmode="numeric"
                autocomplete="off"
                class="tabular-nums"
                bind:value={$form.monthlyTokenBudget}
                aria-invalid={$errors.monthlyTokenBudget ? 'true' : undefined}
                aria-describedby={['token-budget-hint', $errors.monthlyTokenBudget && 'token-budget-error'].filter(Boolean).join(' ')}
              />
              <Field.Description id="token-budget-hint">Empty uses ScreenHire's default.</Field.Description>
              <Field.Error id="token-budget-error" errors={$errors.monthlyTokenBudget?.map((message) => ({ message }))} />
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

      <Panel title="This month">
        <SummaryList>
          <SummaryItem label="Tokens used"><span class="tabular-nums">{tokens.format(detail.tokensUsedThisMonth)}</span></SummaryItem>
        </SummaryList>
      </Panel>
    {/if}
  </div>
</main>
