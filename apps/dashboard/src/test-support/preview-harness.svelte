<script lang="ts">
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';

  // An editor with an Edit / Preview switch and a pop-out preview: one field whose value the preview window follows.
  let mode = $state<'edit' | 'preview'>('edit');
  let title = $state('What is your notice period?');
  const source = createPreviewSource(() => ({ title }));
</script>

<Tooltip.Provider>
  <EditPreviewSwitch bind:mode onpopout={() => source.open('/preview/form')} />
  {#if mode === 'edit'}
    <label>Question <input bind:value={title} /></label>
  {:else}
    <p data-testid="preview">{title}</p>
  {/if}
</Tooltip.Provider>
