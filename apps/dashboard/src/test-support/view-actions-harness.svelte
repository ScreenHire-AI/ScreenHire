<script lang="ts">
  import { createListViews } from '@screenhire/api-client/svelte-query';
  import { UndoOffer } from '#lib/undo.svelte.js';
  import { createViewActions, type ViewActions } from '#lib/views.svelte.js';

  // The sidebar's view actions without the sidebar, as the (app) layout wires them, for tests of what they send.
  let { onready }: { onready: (actions: ViewActions) => void } = $props();

  const views = createListViews();
  const actions = createViewActions({ undo: new UndoOffer(), hiddenBuiltIns: () => views.data?.data.hiddenBuiltIns ?? [] });
  $effect(() => {
    if (views.isSuccess) onready(actions);
  });
</script>
