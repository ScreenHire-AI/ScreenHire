<script lang="ts">
  import SaveIcon from '~icons/lucide/save';
  import { untrack } from 'svelte';
  import type { AiUsageLimitsView, HttpValidationProblemDetails, UsageUnit } from '@screenhire/api-client';
  import {
    ApiError,
    createUpdateAiUsageLimits,
    getGetOrganizationVerificationSettingsQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { unitLabels } from '#lib/ai-usage.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';

  // The Owner's monthly budget and cap (docs/architecture/ai-usage.md). The budget only warns, at 50%, 80% and 100%; the cap
  // pauses the AI-based checks, never the rule-based ones. Both are counted in one unit, tokens or dollars.
  let { limits }: { limits: AiUsageLimitsView } = $props();
  // The form starts from the limits as they were when it opened; saving refetches them, and the person's typing is kept.
  const initial = untrack(() => $state.snapshot(limits));

  const queryClient = useQueryClient();
  const update = createUpdateAiUsageLimits(() => ({
    mutation: {
      onSuccess: () =>
        Promise.all([
          queryClient.invalidateQueries({ queryKey: getGetOrganizationVerificationSettingsQueryKey(), exact: true }),
          invalidatePath(queryClient, '/v1/organization/ai-usage'),
        ]),
    },
  }));

  const amount = z
    .string()
    .trim()
    .regex(/^\d*(\.\d{1,2})?$/, 'Enter a number, or leave it empty.');
  const schema = z.object({ unit: z.enum(['Tokens', 'Money']), budget: amount, cap: amount });

  const text = (value: number | null) => (value === null ? '' : value.toString());
  let saved = $state('');
  const { form, errors, enhance, submitting } = superForm(
    defaults({ unit: initial.unit as UsageUnit, budget: text(initial.budget), cap: text(initial.cap) }, zod4(schema)),
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
        const { unit, budget, cap } = submitted.data;
        try {
          await update.mutateAsync({ data: { unit, budget: budget.trim() ? Number(budget) : null, cap: cap.trim() ? Number(cap) : null } });
          saved = 'Saved. The cap applies from the next check.';
        } catch (error) {
          if (error instanceof ApiError && error.status === 400) {
            applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
          } else {
            submitted.valid = false;
            submitted.errors._errors = [describeError(error, 'Could not save the limits')];
          }
        }
      },
    },
  );

  const message = (list: string[] | undefined) => list?.map((item) => ({ message: item }));
</script>

<form method="POST" use:enhance novalidate aria-label="Monthly AI limits" class="flex flex-col gap-5">
  <p class="text-muted-foreground">
    The budget warns you at 50%, 80% and 100% and stops nothing. The cap pauses the AI-based checks until next month or until you raise it;
    the rule-based checks keep running, and a paused check is never a mark against a candidate.
  </p>

  <Field.Field data-invalid={$errors.unit ? true : undefined}>
    <Field.Label for="limit-unit">Count in</Field.Label>
    <select id="limit-unit" name="unit" class={selectClass} bind:value={$form.unit}>
      <option value="Tokens">{unitLabels.Tokens}</option>
      <option value="Money">{unitLabels.Money} (US$)</option>
    </select>
    <Field.Error errors={message($errors.unit)} />
  </Field.Field>

  <Field.Field data-invalid={$errors.budget ? true : undefined}>
    <Field.Label for="limit-budget">Monthly budget <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
    <Input id="limit-budget" name="budget" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.budget} aria-invalid={$errors.budget ? 'true' : undefined} />
    <Field.Error errors={message($errors.budget)} />
  </Field.Field>

  <Field.Field data-invalid={$errors.cap ? true : undefined}>
    <Field.Label for="limit-cap">Monthly cap <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
    <Input id="limit-cap" name="cap" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.cap} aria-invalid={$errors.cap ? 'true' : undefined} />
    <Field.Description>Empty keeps ScreenHire's default cap for its own key; your own key has none.</Field.Description>
    <Field.Error errors={message($errors.cap)} />
  </Field.Field>

  <div class="flex flex-wrap items-center gap-3">
    <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save limits</Button>
    <p role="status" class="text-body empty:hidden">{saved}</p>
    <FailureText message={$errors._errors?.[0] ?? ''} />
  </div>
</form>
