<script lang="ts">
  import type { Snippet } from 'svelte';

  // What a pop-out preview window shows before its editor's first value (design system A8, E1): the shape of what is
  // coming, or, once no editor has answered (a reloaded window, an editor that closed), one line saying so and what
  // to do, never a skeleton that loads for ever (E3).
  let { orphaned, children }: { orphaned: boolean; children?: Snippet } = $props();
</script>

{#if orphaned}
  <p role="status" class="text-muted-foreground">No editor is open for this preview. Open it from the editor's window icon.</p>
{:else}
  {@render children?.()}
  <p role="status" class="sr-only">Loading the preview</p>
{/if}
