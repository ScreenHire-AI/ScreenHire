<script lang="ts">
  import type { Snippet } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // Confirms one action (design system section 9, A2): the question as the title, what happens in one line, the outline
  // way back and the action, in `destructive` when it destroys. A failure stays in the dialog, in one line.
  let {
    open = $bindable(false),
    title,
    description,
    cancelLabel = 'Cancel',
    confirmLabel,
    icon,
    destructive = true,
    failure: failureLabel,
    onconfirm,
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
    /** Does the work; the dialog closes when it resolves and shows the error when it throws. */
    onconfirm: () => Promise<unknown>;
  } = $props();

  let failure = $state('');
  let busy = $state(false);

  $effect(() => {
    if (open) failure = '';
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
    <FailureText message={failure} />
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>{cancelLabel}</Dialog.Close>
      <Button variant={destructive ? 'destructive' : 'default'} size="lg" disabled={busy} onclick={confirm}>{@render icon?.()}{confirmLabel}</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
