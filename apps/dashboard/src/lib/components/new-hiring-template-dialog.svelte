<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import type { HttpValidationProblemDetails } from '@screenhire/api-client';
  import { ApiError, createCreateHiringTemplate, createListHiringTemplates } from '@screenhire/api-client/svelte-query';
  import { CreateHiringTemplateBody } from '@screenhire/api-client/zod';
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
  import { templatesPath } from '#lib/realtime.js';
  import { templateUsagePath } from '#lib/templates.js';
  import { cn } from '#lib/utils.js';

  // A new hiring template (workflow-engine.md section 1): blank, or a copy of a published version to customise. Version
  // 1 starts as a draft, and the template's page opens. An archived template is out of the pickers, so it is not offered.
  let { open = $bindable(false) }: { open?: boolean } = $props();

  const queryClient = useQueryClient();
  const templates = createListHiringTemplates();
  const create = createCreateHiringTemplate(() => ({
    mutation: { onSettled: () => Promise.all([invalidatePath(queryClient, templatesPath), invalidatePath(queryClient, templateUsagePath)]) },
  }));
  const copyable = $derived((templates.data?.data ?? []).filter((template) => template.latestPublishedVersionId && !template.archived));

  // The select's "a basic pipeline" is an empty string, which the body's uuid would refuse; it becomes null on the way out.
  const schema = CreateHiringTemplateBody.pick({ name: true }).extend({
    name: z.string().trim().min(1, 'Give the template a name.'),
    copyFromVersionId: z.string(),
  });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', copyFromVersionId: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      try {
        const response = await create.mutateAsync({
          data: { name: form.data.name.trim(), copyFromVersionId: form.data.copyFromVersionId || null },
        });
        open = false;
        await goto(`/templates/${response.data.id}`);
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(form, error.body as HttpValidationProblemDetails);
        } else {
          form.valid = false;
          form.errors._errors = [describeError(error, 'Could not create the template')];
        }
      }
    },
  });

  $effect(() => {
    if (open) reset({ data: { name: '', copyFromVersionId: '' } });
  });
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>New hiring template</Dialog.Title>
      <Dialog.Description>Version 1 starts as a draft.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="new-template-name">Name</Field.Label>
        <Input
          id="new-template-name"
          name="name"
          maxlength={120}
          required
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'new-template-name-error' : undefined}
        />
        <Field.Error id="new-template-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      <Field.Field data-invalid={$errors.copyFromVersionId ? true : undefined}>
        <Field.Label for="new-template-source">Start from</Field.Label>
        <select
          id="new-template-source"
          name="copyFromVersionId"
          class={selectClass}
          bind:value={$form.copyFromVersionId}
          aria-invalid={$errors.copyFromVersionId ? 'true' : undefined}
          aria-describedby={$errors.copyFromVersionId ? 'new-template-source-error' : undefined}
        >
          <option value="">A basic pipeline</option>
          {#each copyable as template (template.id)}
            <option value={template.latestPublishedVersionId}>{template.name} (v{template.latestPublishedVersion})</option>
          {/each}
        </select>
        <Field.Error id="new-template-source-error" errors={$errors.copyFromVersionId?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Create template</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
