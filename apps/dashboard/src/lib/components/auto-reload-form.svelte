<script lang="ts">
  import SaveIcon from '~icons/lucide/save';
  import { untrack } from 'svelte';
  import type { AiCreditsView, HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createUpdateAiCreditSettings } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Label } from '#lib/components/ui/label/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { formatCredits } from '#lib/billing.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';

  // The Owner's auto-reload and low-balance alert (ADR-0032). Off by default. "When the balance falls below $X, add $Y", never more
  // than the monthly maximum, charged to the card the last purchase saved. A charge that is refused turns auto-reload off and tells the
  // Owners; it is never retried. Without a saved card it cannot be turned on, and the switch says why beside it (design system A3).
  let { credits }: { credits: AiCreditsView } = $props();
  const initial = untrack(() => $state.snapshot(credits));
  const text = (amount: number | null | undefined) => (amount === null || amount === undefined ? '' : amount.toFixed(2));

  const queryClient = useQueryClient();
  const update = createUpdateAiCreditSettings(() => ({
    mutation: { onSuccess: () => invalidatePath(queryClient, '/v1/organization/billing/credits') },
  }));

  const amount = z
    .string()
    .trim()
    .regex(/^\d*(\.\d{1,2})?$/, 'Enter an amount in dollars.');
  const schema = z.object({ enabled: z.boolean(), below: amount, add: amount, monthlyMax: amount, lowBalanceAlert: amount });
  const toNumber = (value: string) => (value.trim() ? Number(value) : null);

  let saved = $state('');
  const { form, errors, enhance, submitting } = superForm(
    defaults(
      {
        enabled: initial.autoReload.enabled,
        below: text(initial.autoReload.below ?? 5),
        add: text(initial.autoReload.amount ?? initial.amounts[0]),
        monthlyMax: text(initial.autoReload.monthlyMax ?? (initial.amounts[0] ?? 10) * 5),
        lowBalanceAlert: text(initial.lowBalanceAlert),
      },
      zod4(schema),
    ),
    {
      SPA: true,
      dataType: 'json',
      validationMethod: 'submit-only',
      validators: zod4(schema),
      resetForm: false,
      applyAction: false,
      invalidateAll: false,
      async onUpdate({ form: submitted }) {
        saved = '';
        if (!submitted.valid) return;
        const data = submitted.data;
        try {
          await update.mutateAsync({
            data: {
              lowBalanceAlert: toNumber(data.lowBalanceAlert) ?? 0,
              autoReload: { enabled: data.enabled, below: toNumber(data.below), amount: toNumber(data.add), monthlyMax: toNumber(data.monthlyMax) },
            },
          });
          saved = data.enabled ? 'Saved. Auto-reload is on.' : 'Saved.';
        } catch (error) {
          if (error instanceof ApiError && error.status === 400) {
            // The API names its fields after the request (`autoReload.amount`); the form's are flat.
            applyValidationProblem(submitted, error.body as HttpValidationProblemDetails, (field) =>
              field === 'autoReload.amount' ? 'add' : field.replace(/^autoReload\./, ''),
            );
          } else {
            submitted.valid = false;
            submitted.errors._errors = [describeError(error, 'Could not save auto-reload')];
          }
        }
      },
    },
  );

  const message = (list: string[] | undefined) => list?.map((item) => ({ message: item }));
</script>

<form method="POST" use:enhance novalidate aria-label="Auto-reload" class="flex max-w-form flex-col gap-5">
  <div class="flex flex-col gap-1">
    <span class="flex items-center gap-2 pointer-coarse:min-h-11">
      <Switch id="auto-reload-enabled" bind:checked={$form.enabled} disabled={!credits.hasSavedCard && !$form.enabled} aria-describedby="auto-reload-card" />
      <Label for="auto-reload-enabled" class="font-semibold">Auto-reload</Label>
    </span>
    <p id="auto-reload-card" class="max-w-measure text-muted-foreground">
      {credits.hasSavedCard
        ? 'Charges the card saved at your last purchase. A charge that fails turns auto-reload off, and you are told.'
        : 'Buy credits once to save a card. Auto-reload charges that card.'}
    </p>
    <Field.Error errors={message($errors.enabled)} />
  </div>

  {#if $form.enabled}
    <div class="grid gap-5 sm:grid-cols-2">
      <Field.Field data-invalid={$errors.below ? true : undefined}>
        <Field.Label for="auto-reload-below">When the balance falls below</Field.Label>
        <Input id="auto-reload-below" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.below} aria-invalid={$errors.below ? 'true' : undefined} />
        <Field.Error errors={message($errors.below)} />
      </Field.Field>
      <Field.Field data-invalid={$errors.add ? true : undefined}>
        <Field.Label for="auto-reload-amount">Add</Field.Label>
        <Input id="auto-reload-amount" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.add} aria-invalid={$errors.add ? 'true' : undefined} />
        <Field.Error errors={message($errors.add)} />
      </Field.Field>
    </div>
    <Field.Field data-invalid={$errors.monthlyMax ? true : undefined}>
      <Field.Label for="auto-reload-max">Most in a month</Field.Label>
      <Input id="auto-reload-max" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.monthlyMax} aria-invalid={$errors.monthlyMax ? 'true' : undefined} />
      <Field.Description>
        In US dollars, per calendar month. {credits.autoReload.reloadedThisMonth > 0 ? `${formatCredits(credits.autoReload.reloadedThisMonth)} so far this month.` : ''}
      </Field.Description>
      <Field.Error errors={message($errors.monthlyMax)} />
    </Field.Field>
  {/if}

  <Field.Field data-invalid={$errors.lowBalanceAlert ? true : undefined}>
    <Field.Label for="credits-low-alert">Low-balance alert</Field.Label>
    <Input id="credits-low-alert" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.lowBalanceAlert} aria-invalid={$errors.lowBalanceAlert ? 'true' : undefined} />
    <Field.Description>Owners are told once the balance falls below this, in US dollars.</Field.Description>
    <Field.Error errors={message($errors.lowBalanceAlert)} />
  </Field.Field>

  <div class="flex flex-wrap items-center gap-3">
    <Button type="submit" variant="outline" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
    <p role="status" class="text-body empty:hidden">{saved}</p>
    <FailureText message={$errors._errors?.[0] ?? ''} />
  </div>
</form>
