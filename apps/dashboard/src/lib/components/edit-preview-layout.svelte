<script lang="ts">
  import AppWindowIcon from '~icons/lucide/app-window';
  import type { Snippet } from 'svelte';
  import EditPreviewSwitch from '#lib/components/edit-preview-switch.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import { cn } from '#lib/utils.js';

  // An editor whose result someone else sees (design system A8). Where its container has room for the 688px editor and
  // the result at its real size (82rem), the two sit side by side and the preview stays in view while the editor
  // scrolls; narrower, the Edit / Preview switch shows one at a time. Container queries decide, not a measured width,
  // so the first paint is already the right layout and nothing jumps (S8). Both are always rendered, so switching keeps
  // what each holds. `preview` takes the window button for its panel's header: side by side it sits there, narrower it
  // sits beside the switch. `toolbar` shares the switch's row, on the left.
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
    preview: Snippet<[Snippet]>;
    class?: string;
  } = $props();
</script>

{#snippet popout()}
  {#if onpopout}
    <IconButton label="Open in a new window" onclick={onpopout} class="hidden @[82rem]/editor:inline-flex"><AppWindowIcon aria-hidden="true" /></IconButton>
  {/if}
{/snippet}

<div class={cn('@container/editor flex w-full min-w-0 flex-col gap-4', className)} data-edit-preview>
  <div class={['flex min-w-0 flex-wrap items-center justify-between gap-2', !toolbar && '@[82rem]/editor:hidden']}>
    {#if toolbar}<div class="flex min-w-0 flex-wrap items-center gap-2">{@render toolbar()}</div>{/if}
    <EditPreviewSwitch bind:mode {onpopout} class="@[82rem]/editor:hidden" />
  </div>
  <div class="flex min-w-0 flex-col @[82rem]/editor:grid @[82rem]/editor:grid-cols-[minmax(0,43rem)_minmax(0,1fr)] @[82rem]/editor:items-start @[82rem]/editor:gap-6">
    <div class={['min-w-0 flex-col gap-5', mode === 'edit' ? 'flex' : 'hidden @[82rem]/editor:flex']} data-edit-pane>
      {@render editor()}
    </div>
    <div
      class={['min-w-0 flex-col gap-5 @[82rem]/editor:sticky @[82rem]/editor:top-4', mode === 'preview' ? 'flex' : 'hidden @[82rem]/editor:flex']}
      data-preview-pane
    >
      {@render preview(popout)}
    </div>
  </div>
</div>
