<script lang="ts">
  // Every Lucide icon has this one component type.
  import type LucideIcon from '@lucide/svelte/icons/circle';
  import Hint from '#lib/components/hint.svelte';
  import { cn } from '#lib/utils.js';

  // One line of text that ends in an ellipsis when it does not fit, with the full value in a tooltip (design system F5).
  // The tooltip, and its tab stop, exist only while the text is cut. With `href` the text is a link, and the link is the
  // tooltip's trigger, so it stays one tab stop; an `icon` goes before the text, inside the link.
  let {
    text,
    href,
    icon: Icon,
    class: className,
  }: { text: string; href?: string; icon?: typeof LucideIcon; class?: string } = $props();

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
    {#if href}
      <!-- A link already takes focus: the hint's tab stop is not needed. -->
      <a {...props} tabindex={undefined} {href} class={cn('flex min-w-0 items-center gap-1.5 no-underline hover:underline', className)}>
        {#if Icon}<Icon aria-hidden="true" class="shrink-0" />{/if}
        <span bind:this={element} class="min-w-0 truncate">{text}</span>
      </a>
    {:else}
      <span {...props} bind:this={element} class={cn('block min-w-0 truncate', className)}>{text}</span>
    {/if}
  {/snippet}
</Hint>
