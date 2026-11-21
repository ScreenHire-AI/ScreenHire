<script lang="ts">
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { cn } from '#lib/utils.js';

  // Merges a tag into another of the organization's tags, from Settings, Tags: a native select of the others (F2: no
  // script needed to choose). `onmerge` returns a sentence when the API refused it; null closes the dialog.
  let {
    tag,
    others,
    onmerge,
    onclose,
  }: {
    /** The tag that goes away, or null while the dialog is shut. */
    tag: string | null;
    /** The tags it can merge into, most used first. */
    others: readonly string[];
    onmerge: (tag: string, into: string) => Promise<string | null>;
    onclose: () => void;
  } = $props();

  const schema = z.object({ into: z.string().min(1, 'Choose the tag to keep.') });
  const { form, errors, enhance, submitting, reset } = superForm(defaults({ into: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid || !tag) return;
      const failure = await onmerge(tag, form.data.into);
      if (failure) {
        form.valid = false;
        form.errors._errors = [failure];
        return;
      }
      onclose();
    },
  });

  $effect(() => {
    if (tag) reset({ data: { into: '' } });
  });
</script>

<Dialog.Root open={tag !== null} onOpenChange={(open) => !open && onclose()}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Merge "{tag}" into another tag</Dialog.Title>
      <Dialog.Description>Candidates and automations with "{tag}" get the tag you choose instead.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:enhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$errors.into ? true : undefined}>
        <Field.Label for="merge-tag-into">Merge into</Field.Label>
        <select
          id="merge-tag-into"
          name="into"
          class={selectClass}
          bind:value={$form.into}
          aria-invalid={$errors.into ? 'true' : undefined}
          aria-describedby={$errors.into ? 'merge-tag-into-error' : undefined}
        >
          <option value="" disabled>Choose a tag</option>
          {#each others as other (other)}<option value={other}>{other}</option>{/each}
        </select>
        <Field.Error id="merge-tag-into-error" errors={$errors.into?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $errors._errors}
        <FailureText message={$errors._errors[0]} />
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Dialog.Close>
        <Button type="submit" size="lg" disabled={$submitting}>Merge tags</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
