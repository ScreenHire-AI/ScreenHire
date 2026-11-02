<script lang="ts">
  import Trash2Icon from '~icons/lucide/trash-2';
  import UndoNotice from '#lib/components/undo-notice.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { UndoOffer } from '#lib/undo.svelte.js';

  // The offer to undo a change that already happened: nothing is saved here, so Undo succeeds at once.
  const offer = new UndoOffer();
</script>

<Button variant="outline" size="lg" onclick={() => offer.offer('Deleted "Weekly referrals".', async () => null)}>
  <Trash2Icon aria-hidden="true" />Delete view
</Button>
<UndoNotice {offer} />
