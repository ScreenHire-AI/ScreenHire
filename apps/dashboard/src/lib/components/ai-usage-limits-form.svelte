<script lang="ts">
  import SaveIcon from '~icons/lucide/save';
  import { untrack } from 'svelte';
  import type { AiUsageLimitsView, AiUsageLimitView, HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createUpdateAiUsageLimits,
    getGetOrganizationVerificationSettingsQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { formatAmount } from '#lib/ai-usage.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';

  // The Owner's monthly budget and cap (docs/architecture/ai-usage.md). The budget only warns, at 50%, 80% and 100%; the cap
  // pauses the AI-based checks, never the rule-based ones. Both count the dollars billed for AI use, the one amount the Usage tab
  // shows, and are entered in dollars.
  let { limits }: { limits: AiUsageLimitsView } = $props();
  // The form starts from the limits as they were when it opened; saving refetches them, and the person's typing is kept.
  const initial = untrack(() => $state.snapshot(limits));
  // An Owner's own limit starts the field. One stored in tokens before limits were in dollars shows converted (the API does it at
  // the key's price), so saving keeps it, now in dollars. The deployment's default leaves the field empty and is named below it.
  const own = (limit: AiUsageLimitView | null) => (limit && !limit.isDefault && limit.unit === 'Money' ? limit.amount.toFixed(2) : '');
  const defaultFigure = (limit: AiUsageLimitView | null) => (limit?.isDefault ? `, ${formatAmount(limit.amount, limit.unit)} a month,` : '');

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
  const schema = z.object({ budget: amount, cap: amount });

  let saved = $state('');
  const { form, errors, enhance, submitting } = superForm(
    defaults({ budget: own(initial.effectiveBudget), cap: own(initial.effectiveCap) }, zod4(schema)),
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
        const { budget, cap } = submitted.data;
        try {
          await update.mutateAsync({ data: { budget: budget.trim() ? Number(budget) : null, cap: cap.trim() ? Number(cap) : null } });
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

  // With no price a limit cannot be set, but one set before still applies, so it can still be removed (the API always takes removing both).
  const hasOwnLimit = initial.budget !== null || initial.cap !== null;
  let removeOpen = $state(false);
  let removed = $state(false);
  async function removeLimits() {
    await update.mutateAsync({ data: { budget: null, cap: null } });
    removed = true;
  }
</script>

{#if !initial.priced}
  <!-- A limit counts dollars, and a key with no price has nothing to count them at (an own key needs its two prices). -->
  <p class="max-w-measure text-muted-foreground" data-unpriced>
    Limits count dollars, and the AI model in use has no price yet. With your own key, enter its prices in the AI model settings.
  </p>
  {#if hasOwnLimit && !removed}
    <div class="mt-4">
      <Button variant="outline" size="lg" onclick={() => (removeOpen = true)}>Remove limits</Button>
    </div>
    <ConfirmDialog
      bind:open={removeOpen}
      title="Remove your budget and cap?"
      description="ScreenHire's default applies to its own key again, and your own key has none. You can set them again once the model has a price."
      cancelLabel="Keep them"
      confirmLabel="Remove limits"
      failure="Could not remove the limits"
      onconfirm={removeLimits}
    />
  {:else if removed}
    <p role="status" class="mt-4 text-body">Removed.</p>
  {/if}
{:else}
<form method="POST" use:enhance novalidate aria-label="Monthly AI limits" class="flex max-w-form flex-col gap-5">
  <p class="max-w-measure text-muted-foreground">
    In US dollars. The budget warns you at 50%, 80% and 100%. The cap pauses the AI checks, never the others, and never marks a candidate.
  </p>

  <Field.Field data-invalid={$errors.budget ? true : undefined}>
    <Field.Label for="limit-budget">Monthly budget <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
    <Input id="limit-budget" name="budget" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.budget} aria-invalid={$errors.budget ? 'true' : undefined} />
    <Field.Description>Empty keeps ScreenHire's default budget{defaultFigure(initial.effectiveBudget)} for its own key; your own key has none.</Field.Description>
    <Field.Error errors={message($errors.budget)} />
  </Field.Field>

  <Field.Field data-invalid={$errors.cap ? true : undefined}>
    <Field.Label for="limit-cap">Monthly cap <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
    <Input id="limit-cap" name="cap" inputmode="decimal" autocomplete="off" class="tabular-nums" bind:value={$form.cap} aria-invalid={$errors.cap ? 'true' : undefined} />
    <Field.Description>Empty keeps ScreenHire's default cap{defaultFigure(initial.effectiveCap)} for its own key; your own key has none.</Field.Description>
    <Field.Error errors={message($errors.cap)} />
  </Field.Field>

  <div class="flex flex-wrap items-center gap-3">
    <Button type="submit" variant="outline" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save limits</Button>
    <p role="status" class="text-body empty:hidden">{saved}</p>
    <FailureText message={$errors._errors?.[0] ?? ''} />
  </div>
</form>
{/if}
