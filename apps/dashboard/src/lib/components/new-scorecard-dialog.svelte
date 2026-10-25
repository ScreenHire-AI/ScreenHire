<script lang="ts">
  import PlusIcon from '@lucide/svelte/icons/plus';
  import type { HttpValidationProblemDetails, ScorecardQuestion } from '@screenhire/api-client';
  import { ApiError, createCreateScorecardTemplate, createListScorecardTemplates, getGetScorecardTemplateQueryOptions } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { maxNameLength, scorecardTemplatesPath, templateHref, templateUsagePath } from '#lib/templates.js';
  import { cn } from '#lib/utils.js';

  // A new scorecard of the team's own: a name, and a blank scorecard or a copy of one in the library to start from. The
  // API needs at least one question, so a blank scorecard starts with one rating question to rename. The editor opens.
  let { open = $bindable(false) }: { open?: boolean } = $props();

  /** The question a blank scorecard starts with. */
  const firstQuestion: ScorecardQuestion = { id: 'question', type: 'Rating5', label: 'New question', required: false, guidance: null, options: null };

  const queryClient = useQueryClient();
  const library = createListScorecardTemplates();
  const create = createCreateScorecardTemplate(() => ({
    mutation: { onSettled: () => Promise.all([invalidatePath(queryClient, scorecardTemplatesPath), invalidatePath(queryClient, templateUsagePath)]) },
  }));
  const sources = $derived((library.data?.data ?? []).filter((scorecard) => !scorecard.archived));

  const schema = z.object({
    name: z.string().trim().min(1, 'Name the scorecard.').max(maxNameLength.scorecards, `Keep the name to ${maxNameLength.scorecards} characters or fewer.`),
    copiedFrom: z.string(),
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', copiedFrom: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      try {
        const source = form.data.copiedFrom ? (await queryClient.fetchQuery(getGetScorecardTemplateQueryOptions(form.data.copiedFrom))).data : undefined;
        const response = await create.mutateAsync({
          data: { name: form.data.name.trim(), questions: source?.questions ?? [firstQuestion], copiedFrom: source?.id ?? null },
        });
        open = false;
        await goto(templateHref('scorecards', response.data.id));
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(form, error.body as HttpValidationProblemDetails);
        } else {
          form.valid = false;
          form.errors._errors = [describeError(error, 'Could not create the scorecard')];
        }
      }
    },
  });

  $effect(() => {
    if (open) reset({ data: { name: '', copiedFrom: '' } });
  });
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>New scorecard</Dialog.Title>
      <Dialog.Description>Stages in your hiring templates can then use it.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="new-scorecard-name">Name</Field.Label>
        <Input
          id="new-scorecard-name"
          name="name"
          maxlength={maxNameLength.scorecards}
          required
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'new-scorecard-name-error' : undefined}
        />
        <Field.Error id="new-scorecard-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      <Field.Field>
        <Field.Label for="new-scorecard-source">Start from</Field.Label>
        <select id="new-scorecard-source" name="copiedFrom" class={selectClass} bind:value={$form.copiedFrom}>
          <option value="">A blank scorecard</option>
          {#each sources as scorecard (scorecard.id)}
            <option value={scorecard.id}>{scorecard.name}</option>
          {/each}
        </select>
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Create scorecard</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
