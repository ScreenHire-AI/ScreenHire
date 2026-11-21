<script lang="ts">
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { cn } from '#lib/utils.js';

  // Renames a tag everywhere, from Settings, Tags: one field. `onrename` returns a sentence when the API refused it (the
  // name is another tag already, which Merge is for), shown under the field; null closes the dialog.
  let {
    tag,
    onrename,
    onclose,
  }: {
    /** The tag being renamed, or null while the dialog is shut. */
    tag: string | null;
    onrename: (tag: string, name: string) => Promise<string | null>;
    onclose: () => void;
  } = $props();

  // As the API allows a tag (JobApplication.MaxTagLength).
  const schema = z.object({ name: z.string().trim().min(1, 'Name the tag.').max(50, 'Use at most 50 characters.') });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ name: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid || !tag) return;
      const failure = await onrename(tag, form.data.name.trim());
      if (failure) {
        form.valid = false;
        form.errors._errors = [failure];
        return;
      }
      onclose();
    },
  });

  $effect(() => {
    if (tag) reset({ data: { name: tag } });
  });
</script>

<Dialog.Root open={tag !== null} onOpenChange={(open) => !open && onclose()}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Rename tag</Dialog.Title>
      <Dialog.Description>Every candidate and every automation that names it changes too.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.name ? true : undefined}>
        <Field.Label for="rename-tag-name">Name</Field.Label>
        <Input
          id="rename-tag-name"
          name="name"
          maxlength={50}
          required
          autocomplete="off"
          bind:value={$form.name}
          aria-invalid={$errors.name ? 'true' : undefined}
          aria-describedby={$errors.name ? 'rename-tag-name-error' : undefined}
        />
        <Field.Error id="rename-tag-name-error" errors={$errors.name?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}>Rename tag</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
