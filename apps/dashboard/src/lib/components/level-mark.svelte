<script lang="ts" module>
  // The mark's colour: the status's `-mark` token, which dark mode redefines (design system sections 3.2 and 3.3).
  const colours = { review: 'text-status-review-mark', verify: 'text-status-verify-mark' } as const;
</script>

<script lang="ts">
  import { cn } from '#lib/utils.js';

  // The level mark (design system section 14, I1): three small squares filled to the level, two for Review and three for
  // Verify. It is the one custom glyph and the icon of those two states, so the count of filled steps tells them apart
  // without colour (T1). Decorative: the state's word and its screen-reader text carry the meaning, so it is hidden from
  // assistive technology. It is built from CSS squares in status tokens, so it needs no exemption from tokens-only.
  let {
    steps,
    tone,
    size = 'sm',
    class: className,
  }: {
    /** How many of the three squares are filled. */
    steps: 0 | 1 | 2 | 3;
    tone: 'review' | 'verify';
    size?: 'sm' | 'lg';
    class?: string;
  } = $props();
</script>

<span aria-hidden="true" data-level-mark={steps} class={cn('inline-flex shrink-0 items-center gap-0.5', colours[tone], className)}>
  {#each [1, 2, 3] as step (step)}
    <span class={['rounded-xs border border-current', size === 'lg' ? 'size-2' : 'size-1.5', step <= steps && 'bg-current']}></span>
  {/each}
</span>
