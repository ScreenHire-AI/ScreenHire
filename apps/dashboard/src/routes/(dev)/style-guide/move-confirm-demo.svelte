<script lang="ts">
  import MoveConfirmDialog from '#lib/components/move-confirm-dialog.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import type { MoveConfirmation, Moves } from '#lib/moves.svelte.js';

  // The move confirmation with nothing behind it: the question a move into Technical exercise asks, answered by either button.
  let confirmation = $state<MoveConfirmation | null>(null);
  let answered = $state('');
  const moves = {
    get confirmation() {
      return confirmation;
    },
    answer(moved: boolean) {
      answered = moved ? 'Moved.' : 'Cancelled.';
      confirmation = null;
    },
  } as unknown as Moves;

  function ask() {
    answered = '';
    confirmation = {
      title: 'Move Joel Mensah to Technical exercise?',
      intro: 'Moving them there will:',
      lines: ['Send the "Take-home exercise" email.', 'Create a task and add the tag "Exercise sent".'],
      restoreFocusTo: null,
    };
  }
</script>

<Button variant="outline" size="lg" onclick={ask}>Move to Technical exercise</Button>
<p role="status" class="mt-2 text-body text-muted-foreground empty:hidden">{answered}</p>
<MoveConfirmDialog {moves} />
