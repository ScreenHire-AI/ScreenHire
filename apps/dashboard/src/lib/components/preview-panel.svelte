<script lang="ts">
  import AppWindowIcon from '~icons/lucide/app-window';
  import type { Snippet } from 'svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // An editor's Preview panel (design system A8): what someone else sees, and in its header the editor's own actions, then
  // Open in a new window, which pops the preview out into a second window that follows the edits
  // (#lib/preview-window.svelte.ts). The window is for this panel's content, so its button is here and not beside the
  // Edit / Preview switch, which holds two choices only. A phone has no room for a second window, so it has no button.
  let {
    onpopout,
    actions,
    children,
  }: {
    /** Opens the preview window, usually `source.open(url)`. */
    onpopout?: () => void;
    /** The editor's own actions on the preview, such as Send me a test. */
    actions?: Snippet;
    children: Snippet;
  } = $props();
</script>

{#snippet controls()}
  {@render actions?.()}
  {#if onpopout}
    <Button variant="outline" onclick={onpopout} class="max-md:hidden"><AppWindowIcon aria-hidden="true" />Open in a new window</Button>
  {/if}
{/snippet}

<Panel title="Preview" actions={actions || onpopout ? controls : undefined} {children} />
