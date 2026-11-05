<script lang="ts">
  import type { Snippet } from 'svelte';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import { cn } from '#lib/utils.js';

  // An editor whose result someone else sees (design system A8): the Edit / Preview switch shows one at a time at every
  // width, so the editor has the room and the person's attention and the preview is one press away. Both are always
  // rendered, so switching keeps what each holds. `toolbar` shares the switch's row, on the left.
  let {
    mode = $bindable('edit'),
    onpopout,
    toolbar,
    editor,
    preview,
    class: className,
  }: {
    mode?: 'edit' | 'preview';
    /** Opens the preview in a second window that follows the edits (#lib/preview-window.svelte.ts). */
    onpopout?: () => void;
    toolbar?: Snippet;
    editor: Snippet;
    preview: Snippet;
    class?: string;
  } = $props();
</script>

<div class={cn('@container/editor flex w-full min-w-0 flex-col gap-4', className)} data-edit-preview>
  <!-- The row keeps the edit pane's width, so the switch stays with its form rather than at the window's far edge (S6, S7). -->
  <div class="flex max-w-form min-w-0 flex-wrap items-center justify-between gap-2 @[40rem]/editor:flex-nowrap">
    {#if toolbar}<div class="flex min-w-0 flex-wrap items-center gap-2 @[40rem]/editor:flex-1 @[40rem]/editor:flex-nowrap">{@render toolbar()}</div>{/if}
    <EditPreviewSwitch bind:mode {onpopout} class="shrink-0" />
  </div>
  <div class={['max-w-form min-w-0 flex-col gap-5', mode === 'edit' ? 'flex' : 'hidden']} data-edit-pane>
    {@render editor()}
  </div>
  <div class={['min-w-0 flex-col gap-5', mode === 'preview' ? 'flex' : 'hidden']} data-preview-pane>
    {@render preview()}
  </div>
</div>
