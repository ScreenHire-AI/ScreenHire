<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import type { FormKind, HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createCreateFormTemplate, createListFormTemplates } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { firstFormQuestion } from '#lib/form-library.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { formTemplatesPath, maxNameLength, templateHref, templateUsagePath } from '#lib/templates.js';
  import { cn } from '#lib/utils.js';

  // A new library form (workflow-engine.md section 7, Forms): a name, what kind it is where this deployment offers
  // equality monitoring forms, and a blank form or a copy of one to start from. The API needs a question, so a blank form
  // starts with one to rename. Its editor opens, carrying `returnTo` when a hiring template asked for the form.
  let { open = $bindable(false), returnTo }: { open?: boolean; returnTo?: string } = $props();

  const queryClient = useQueryClient();
  const library = createListFormTemplates();
  const create = createCreateFormTemplate(() => ({
    mutation: { onSettled: () => Promise.all([invalidatePath(queryClient, formTemplatesPath), invalidatePath(queryClient, templateUsagePath)]) },
  }));
  const sources = $derived((library.data?.data.forms ?? []).filter((form) => !form.archived));
  const offersEquality = $derived(Boolean(library.data?.data.equalityMonitoringEnabled));

  // A copy takes its source's name with "(copy)", so only a blank form needs one.
  const schema = z
    .object({
      name: z.string().trim().max(maxNameLength.forms, `Keep the name to ${maxNameLength.forms} characters or fewer.`),
      kind: z.enum(['Standard', 'EqualityMonitoring']),
      copyFrom: z.string(),
    })
    .refine((data) => data.copyFrom !== '' || data.name.length > 0, { message: 'Name the form.', path: ['name'] });
  const empty = { name: '', kind: 'Standard' as FormKind, copyFrom: '' };
  const { form, errors, enhance, submitting, reset } = superForm(defaults(empty, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      try {
        const name = form.data.name.trim();
        const response = form.data.copyFrom
          ? await create.mutateAsync({ data: { copyFromFormId: form.data.copyFrom } })
          : await create.mutateAsync({ data: { kind: form.data.kind, content: { name, questions: [firstFormQuestion(form.data.kind)] } } });
        open = false;
        const href = templateHref('forms', response.data.id);
        await goto(returnTo ? `${href}?for=${encodeURIComponent(returnTo)}` : href);
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(form, error.body as HttpValidationProblemDetails);
        } else {
          form.valid = false;
          form.errors._errors = [describeError(error, 'Could not create the form')];
        }
      }
    },
  });

  $effect(() => {
    if (open) reset({ data: empty });
  });
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>New form</Sheet.Title>
      <Sheet.Description>Hiring templates can then send it at a stage, or you can send it by hand.</Sheet.Description>
    </Sheet.Header>
    <form method="POST" use:enhance novalidate class="flex flex-1 flex-col gap-5 px-4 pb-4">
      <Field.Field>
        <Field.Label for="new-form-source">Start from</Field.Label>
        <select id="new-form-source" name="copyFrom" class={selectClass} bind:value={$form.copyFrom}>
          <option value="">A blank form</option>
          {#each sources as source (source.id)}
            <option value={source.id}>A copy of {source.name}</option>
          {/each}
        </select>
      </Field.Field>
      {#if !$form.copyFrom}
        <Field.Field data-invalid={$errors.name ? true : undefined}>
          <Field.Label for="new-form-name">Name</Field.Label>
          <Input
            id="new-form-name"
            name="name"
            maxlength={maxNameLength.forms}
            required
            bind:value={$form.name}
            aria-invalid={$errors.name ? 'true' : undefined}
            aria-describedby={$errors.name ? 'new-form-name-error' : undefined}
          />
          <Field.Error id="new-form-name-error" errors={$errors.name?.map((message) => ({ message }))} />
        </Field.Field>
        {#if offersEquality}
          <Field.Field>
            <Field.Label for="new-form-kind">Kind</Field.Label>
            <select id="new-form-kind" name="kind" class={selectClass} bind:value={$form.kind}>
              <option value="Standard">A form the team reads</option>
              <option value="EqualityMonitoring">Equality monitoring: only counted, never read one by one</option>
            </select>
          </Field.Field>
        {/if}
      {/if}
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Sheet.Footer class="p-0">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Create form</Button>
      </Sheet.Footer>
    </form>
  </Sheet.Content>
</Sheet.Root>
