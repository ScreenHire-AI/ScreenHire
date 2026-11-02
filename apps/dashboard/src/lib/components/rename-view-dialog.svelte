<script lang="ts">
  import type { SavedViewSummary } from '@screenhire/api-client';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { cn } from '#lib/utils.js';

  // Renames a saved view from the sidebar menu: one field. `onrename` returns a sentence when the API refused it (a name
  // already in use), which shows under the field; null closes the dialog.
  let {
    view,
    onrename,
    onclose,
  }: {
    /** The view being renamed, or null while the dialog is shut. */
    view: SavedViewSummary | null;
    onrename: (view: SavedViewSummary, name: string) => Promise<string | null>;
    onclose: () => void;
  } = $props();

  const schema = z.object({ name: z.string().trim().min(1, 'Name the view.').max(80, 'Use at most 80 characters.') });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid || !view) return;
      const failure = await onrename(view, form.data.name.trim());
      if (failure) {
        form.valid = false;
        form.errors._errors = [failure];
        return;
      }
      onclose();
    },
  });

  $effect(() => {
    if (view) reset({ data: { name: view.name } });
  });
</script>

<Dialog.Root open={view !== null} onOpenChange={(open) => !open && onclose()}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Rename view</Dialog.Title>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="rename-view-name">Name</Field.Label>
        <Input
          id="rename-view-name"
          name="name"
          maxlength={80}
          required
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'rename-view-name-error' : undefined}
        />
        <Field.Error id="rename-view-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}>Rename view</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
