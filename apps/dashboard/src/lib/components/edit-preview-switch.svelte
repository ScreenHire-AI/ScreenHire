<script lang="ts">
  import AppWindowIcon from '@lucide/svelte/icons/app-window';
  import EyeIcon from '@lucide/svelte/icons/eye';
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import IconButton from '#lib/components/icon-button.svelte';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { cn } from '#lib/utils.js';

  // The Edit / Preview switch (design system A8), at the top of an editor whose result someone else sees: a pencil and
  // an eye, in a labelled group where the pressed one is the view on screen. Editor and preview never share the screen.
  // With `onpopout`, a window icon beside it opens the preview in a second window that follows the edits
  // (#lib/preview-window.svelte.ts).
  let {
    mode = $bindable('edit'),
    onpopout,
    class: className,
  }: {
    mode?: 'edit' | 'preview';
    /** Opens the preview window, usually `source.open(url)`. */
    onpopout?: () => void;
    class?: string;
  } = $props();
</script>

<div class={cn('flex items-center gap-1', className)}>
  <div role="group" aria-label="View" class="flex gap-1">
    <Toggle bind:pressed={() => mode === 'edit', () => (mode = 'edit')}><PencilIcon aria-hidden="true" />Edit</Toggle>
    <Toggle bind:pressed={() => mode === 'preview', () => (mode = 'preview')}><EyeIcon aria-hidden="true" />Preview</Toggle>
  </div>
  {#if onpopout}
    <IconButton label="Open in a new window" onclick={onpopout}><AppWindowIcon aria-hidden="true" /></IconButton>
  {/if}
</div>
