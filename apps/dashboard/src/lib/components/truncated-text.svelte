<script lang="ts">
  import Hint from '#lib/components/hint.svelte';
  import { cn } from '#lib/utils.js';

  // One line of text that ends in an ellipsis when it does not fit, with the full value in a tooltip (design system F5).
  // The tooltip, and its tab stop, exist only while the text is cut.
  let { text, class: className }: { text: string; class?: string } = $props();

  let element = $state<HTMLElement | null>(null);
  let truncated = $state(false);

  $effect(() => {
    if (!element) return;
    void text;
    const target = element;
    const measure = () => (truncated = target.scrollWidth > target.clientWidth);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(target);
    return () => observer.disconnect();
  });
</script>

<Hint content={text} disabled={!truncated}>
  {#snippet children({ props })}
    <span {...props} bind:this={element} class={cn('block min-w-0 truncate', className)}>{text}</span>
  {/snippet}
</Hint>
