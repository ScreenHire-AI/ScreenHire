<script lang="ts">
  // Every Lucide icon has this one component type.
  import type LucideIcon from '~icons/lucide/circle';
  import Hint from '#lib/components/hint.svelte';
  import { cn } from '#lib/utils.js';

  // One line of text (or `lines` of them) that ends in an ellipsis when it does not fit, with the full value in a tooltip
  // (design system F5).
  // The tooltip, and its tab stop, exist only while the text is cut. With `href` the text is a link, such as a name in a
  // table, and its tooltip opens on the link itself. A link outside a list of names is `underlined` at rest (A5) and may
  // take an `icon` that goes before its text, inside the link.
  let {
    text,
    href,
    icon: Icon,
    underlined = false,
    lines = 1,
    class: className,
    ...restProps
  }: {
    text: string;
    href?: string;
    icon?: typeof LucideIcon;
    underlined?: boolean;
    /** Two wraps the text onto a second line before cutting it, as a workflow graph node's title. Not for links. */
    lines?: 1 | 2;
    class?: string;
    /** -1 inside a card that is itself the tab stop, such as a board card. */
    tabindex?: number;
    /** A link's click, such as closing the sheet it sits in. */
    onclick?: (event: MouseEvent) => void;
  } & Record<`data-${string}`, unknown> = $props();

  let element = $state<HTMLElement | null>(null);
  let truncated = $state(false);

  $effect(() => {
    if (!element) return;
    void text;
    const target = element;
    const measure = () => (truncated = lines === 2 ? target.scrollHeight > target.clientHeight : target.scrollWidth > target.clientWidth);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(target);
    return () => observer.disconnect();
  });
</script>

<Hint content={text} disabled={!truncated}>
  {#snippet children({ props })}
    {#if href && Icon}
      <!-- The icon sits inside the link, so the text is the part that is cut. -->
      <a {...props} {...restProps} {href} class={cn('flex min-w-0 items-center gap-1.5', className)}>
        <Icon aria-hidden="true" class="shrink-0" />
        <span bind:this={element} class={cn('min-w-0 truncate', underlined && 'underline underline-offset-2')}>{text}</span>
      </a>
    {:else if href}
      <a {...props} {...restProps} {href} bind:this={element} class={cn('block min-w-0 truncate', underlined && 'underline underline-offset-2', className)}>{text}</a>
    {:else}
      <span {...props} {...restProps} bind:this={element} class={cn('block min-w-0', lines === 2 ? 'line-clamp-2 break-words' : 'truncate', className)}>{text}</span>
    {/if}
  {/snippet}
</Hint>
