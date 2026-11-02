<script lang="ts">
  import type { HttpValidationProblemDetails, ViewFilters, ViewScope } from '@screenhire/api-client';
  import { ApiError, createCreateView } from '@screenhire/api-client/svelte-query';
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
  import { Label } from '#lib/components/ui/label/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { viewsPath } from '#lib/realtime.js';
  import { viewHref } from '#lib/sidebar-views.js';
  import { cn } from '#lib/utils.js';

  // The Save view dialog (docs/architecture/saved-views.md): a name, and Share with the team, off by default. The new view
  // joins the sidebar's list and opens, which makes it the view the table is on.
  let {
    open = $bindable(false),
    scope,
    jobId = null,
    filters,
  }: {
    open?: boolean;
    scope: ViewScope;
    /** The job whose table it is, for a job's view. */
    jobId?: string | null;
    /** What the table filters and sorts by now. */
    filters: ViewFilters;
  } = $props();

  const queryClient = useQueryClient();
  const create = createCreateView(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, viewsPath) } }));

  const schema = z.object({ name: z.string().trim().min(1, 'Name the view.').max(80, 'Use at most 80 characters.'), shared: z.boolean() });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '', shared: false }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      try {
        const response = await create.mutateAsync({
          data: { name: form.data.name.trim(), scope, jobId, shared: form.data.shared, filters },
        });
        open = false;
        await goto(viewHref(response.data));
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(form, error.body as HttpValidationProblemDetails);
        } else {
          form.valid = false;
          form.errors._errors = [describeError(error, 'Could not save the view')];
        }
      }
    },
  });

  $effect(() => {
    if (open) reset({ data: { name: '', shared: false } });
  });
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Save view</Dialog.Title>
      <Dialog.Description>Keeps these filters and this sort in the sidebar.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="save-view-name">Name</Field.Label>
        <Input
          id="save-view-name"
          name="name"
          maxlength={80}
          required
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'save-view-name-error' : undefined}
        />
        <Field.Error id="save-view-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      <div class="flex items-center justify-between gap-4">
        <Label for="save-view-shared" class="font-normal">Share with the team</Label>
        <Switch id="save-view-shared" bind:checked={$form.shared} />
      </div>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}>Save view</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
