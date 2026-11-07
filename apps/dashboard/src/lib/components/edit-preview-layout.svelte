<script lang="ts">
  import type { Snippet } from 'svelte';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import { cn } from '#lib/utils.js';

  // An editor whose result someone else sees (design system A8): the Edit / Preview switch shows one at a time at every
  // width, so the editor has the room and the person's attention and the preview is one press away. Both are always
  // rendered, so switching keeps what each holds. `toolbar` shares the switch's row, on the left. The edit pane is the
  // 688px form column unless `wide` is set, for an editor that lays its panels out in rows (Branding) and takes the work
  // area's width; the preview always does. `actions` sit at the row's right end and keep the row in view while the page
  // scrolls, for an editor whose Save is not in a header band (Branding).
  let {
    mode = $bindable('edit'),
    onpopout,
    wide = false,
    toolbar,
    actions,
    editor,
    preview,
    class: className,
  }: {
    mode?: 'edit' | 'preview';
    /** Opens the preview in a second window that follows the edits (#lib/preview-window.svelte.ts). */
    onpopout?: () => void;
    /** The edit pane and the switch's row take the work area's width instead of the form column's. */
    wide?: boolean;
    toolbar?: Snippet;
    actions?: Snippet;
    editor: Snippet;
    preview: Snippet;
    class?: string;
  } = $props();
</script>

<div class={cn('@container/editor flex w-full min-w-0 flex-col gap-4', className)} data-edit-preview>
  <!-- The row keeps the edit pane's width, so the switch stays with its form rather than at the window's far edge (S6, S7). -->
  <div
    class={[
      'flex min-w-0 flex-wrap items-center justify-between gap-2 @[40rem]/editor:flex-nowrap',
      !wide && 'max-w-form',
      actions && 'sticky top-0 z-10 -my-2 bg-background py-2',
    ]}
    data-edit-preview-row
  >
    {#if toolbar}<div class="flex min-w-0 flex-wrap items-center gap-2 @[40rem]/editor:flex-1 @[40rem]/editor:flex-nowrap">{@render toolbar()}</div>{/if}
    <EditPreviewSwitch bind:mode {onpopout} class="shrink-0" />
    {#if actions}<div class="ml-auto flex min-w-0 flex-wrap items-center justify-end gap-x-3 gap-y-1">{@render actions()}</div>{/if}
  </div>
  <div class={['min-w-0 flex-col gap-5', !wide && 'max-w-form', mode === 'edit' ? 'flex' : 'hidden']} data-edit-pane>
    {@render editor()}
  </div>
  <div class={['min-w-0 flex-col gap-5', mode === 'preview' ? 'flex' : 'hidden']} data-preview-pane>
    {@render preview()}
  </div>
</div>
