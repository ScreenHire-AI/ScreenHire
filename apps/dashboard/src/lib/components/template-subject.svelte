<script lang="ts">
  import Hint from '#lib/components/hint.svelte';

  // An email template's subject on one line (L1): plain text with each `{{ variable }}` as a muted word ("job title"),
  // cut with an ellipsis when it does not fit and whole in a tooltip, as truncated text is (F5).
  let { parts }: { parts: readonly { text: string; variable: boolean }[] } = $props();

  const whole = $derived(parts.map((part) => part.text).join(''));
  let element = $state<HTMLElement | null>(null);
  let truncated = $state(false);

  $effect(() => {
    if (!element) return;
    void whole;
    const target = element;
    const measure = () => (truncated = target.scrollWidth > target.clientWidth);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(target);
    return () => observer.disconnect();
  });
</script>

<Hint content={whole} disabled={!truncated}>
  {#snippet children({ props })}
    <span {...props} bind:this={element} class="block min-w-0 truncate text-foreground"
      >{#each parts as part, index (index)}{#if part.variable}<span class="text-muted-foreground">{part.text}</span>{:else}{part.text}{/if}{/each}</span
    >
  {/snippet}
</Hint>
