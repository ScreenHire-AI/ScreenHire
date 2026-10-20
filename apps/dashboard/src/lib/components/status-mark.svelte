<script lang="ts" module>
  import type { MarkShape, MarkTone } from '#lib/distribution.js';

  const shapes: Record<MarkShape, string> = {
    circle: 'rounded-full',
    diamond: 'rotate-45 scale-[0.85]',
    square: '',
    ring: 'rounded-full border-[1.5px] bg-transparent',
  };

  const fills: Record<MarkTone, string> = {
    clear: 'bg-status-clear-mark border-status-clear-mark',
    review: 'bg-status-review-mark border-status-review-mark',
    verify: 'bg-status-verify-mark border-status-verify-mark',
    neutral: 'bg-muted-foreground border-muted-foreground',
    primary: 'bg-primary border-primary',
  };
</script>

<script lang="ts">
  import { cn } from '#lib/utils.js';

  // The shape half of "shape + colour + label" (design-system section 2.2). Decorative: the label next to it carries
  // the meaning, so it is hidden from assistive technology.
  let { shape, tone, class: className = 'size-2' }: { shape: MarkShape; tone: MarkTone; class?: string } = $props();
</script>

<span aria-hidden="true" data-shape={shape} class={cn('inline-block shrink-0', fills[tone], shapes[shape], className)}></span>
