<script lang="ts">
  import type { Snippet } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // Confirms one action (design system section 9, A2): the question as the title, what happens in one line, the outline
  // way back and the action, in `destructive` when it destroys. A failure stays in the dialog, in one line. An action that
  // cannot be undone and takes someone's data with it asks to be typed first (`confirmText`): the action stays off until the
  // words match, ignoring case and spaces at the ends.
  let {
    open = $bindable(false),
    title,
    description,
    cancelLabel = 'Cancel',
    confirmLabel,
    icon,
    destructive = true,
    failure: failureLabel,
    confirmText,
    onconfirm,
    children,
  }: {
    open?: boolean;
    title: string;
    description: string;
    cancelLabel?: string;
    confirmLabel: string;
    /** The action's icon, before its label. */
    icon?: Snippet;
    destructive?: boolean;
    /** What failed, for `describeError`: "Could not revoke the key". */
    failure: string;
    /** Words the person must type before the action is offered, such as the name of what is destroyed. */
    confirmText?: string;
    /** Does the work; the dialog closes when it resolves and shows the error when it throws. */
    onconfirm: () => Promise<unknown>;
    /** A choice that goes with the action, such as also revoking a removed member's keys. */
    children?: Snippet;
  } = $props();

  let failure = $state('');
  let busy = $state(false);
  let typed = $state('');
  const matches = $derived(confirmText === undefined || typed.trim().toLowerCase() === confirmText.trim().toLowerCase());

  $effect(() => {
    if (open) {
      failure = '';
      typed = '';
    }
  });

  async function confirm() {
    failure = '';
    busy = true;
    try {
      await onconfirm();
      open = false;
    } catch (error) {
      failure = describeError(error, failureLabel);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>{title}</Dialog.Title>
      <Dialog.Description>{description}</Dialog.Description>
    </Dialog.Header>
    {@render children?.()}
    {#if confirmText !== undefined}
      <Field.Field>
        <Field.Label for="confirm-dialog-typed">Type <strong class="font-semibold">{confirmText}</strong> to confirm</Field.Label>
        <Input id="confirm-dialog-typed" bind:value={typed} autocomplete="off" spellcheck={false} />
      </Field.Field>
    {/if}
    <FailureText message={failure} />
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>{cancelLabel}</Dialog.Close>
      <Button variant={destructive ? 'destructive' : 'default'} size="lg" disabled={busy || !matches} onclick={confirm}>{@render icon?.()}{confirmLabel}</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
