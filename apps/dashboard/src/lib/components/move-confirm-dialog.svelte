<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import { onDestroy, tick } from 'svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Checkbox } from '#lib/components/ui/checkbox/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import type { MoveConfirmation, Moves } from '#lib/moves.svelte.js';

  // Asks before a move into a stage whose automations send an email, create a task or the like, and says what they do, in the
  // Automations map's words (design system section 11, Move confirmation). `moves` holds the question; every move control
  // that goes through it (menu, keyboard, drag, bulk) gets this one dialog. Escape, the close button and Cancel are all
  // Cancel, which does nothing and gives the focus back to what had it. "Don't ask again" is the person's, in this browser,
  // and the account menu turns it back on.
  let { moves }: { moves: Moves } = $props();

  const question = $derived(moves.confirmation);
  let dontAskAgain = $state(false);
  // The last question stays drawn while the dialog fades out, so closing never flashes an empty box.
  let shown = $state<MoveConfirmation | null>(null);
  $effect(() => {
    if (question) {
      shown = question;
      dontAskAgain = false;
    }
  });

  async function answer(moved: boolean) {
    const back = shown?.restoreFocusTo;
    moves.answer(moved, dontAskAgain);
    // After Move the move control puts the focus on the card in its new place; after Cancel it stays where it was.
    if (!moved) {
      await tick();
      if (back?.isConnected) back.focus();
    }
  }

  // Leaving the page while the question is open is Cancel, so the move that waits on it ends.
  onDestroy(() => {
    if (moves.confirmation) moves.answer(false);
  });
</script>

<Dialog.Root
  open={question !== null}
  onOpenChange={(open) => {
    if (!open && moves.confirmation) void answer(false);
  }}
>
  <Dialog.Content class="sm:max-w-md" onCloseAutoFocus={(event) => event.preventDefault()}>
    <Dialog.Header>
      <Dialog.Title>{shown?.title}</Dialog.Title>
      <Dialog.Description>{shown?.intro}</Dialog.Description>
    </Dialog.Header>
    <ul aria-label="What happens" class="flex list-disc flex-col gap-1.5 pl-5 text-body">
      {#each shown?.lines ?? [] as line (line)}<li>{line}</li>{/each}
    </ul>
    <label class="flex min-h-9 items-center gap-2 text-body pointer-coarse:min-h-11">
      <Checkbox bind:checked={dontAskAgain} />
      Don't ask again
    </label>
    <Dialog.Footer>
      <Button variant="outline" size="lg" onclick={() => answer(false)}>Cancel</Button>
      <Button size="lg" onclick={() => answer(true)}><ArrowRightIcon aria-hidden="true" />Move</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
