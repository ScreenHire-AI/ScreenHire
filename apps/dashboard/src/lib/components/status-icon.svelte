<script lang="ts" module>
  import type { StatusTone } from '#lib/status.js';

  // Each status's icon colour (design system sections 3.2 and 3.3): the `-mark` tokens, which dark mode redefines.
  export const iconColours: Record<StatusTone, string> = {
    clear: 'text-status-clear-mark',
    review: 'text-status-review-mark',
    verify: 'text-status-verify-mark',
    ai: 'text-status-ai-mark',
    neutral: 'text-muted-foreground',
  };
</script>

<script lang="ts">
  import { statuses, type StatusName } from '#lib/status.js';
  import { cn } from '#lib/utils.js';

  // The icon half of "icon, colour and label" (design system section 3.2). Decorative: the label next to it carries the
  // meaning, so it is hidden from assistive technology. Only status badges and a status's saved view use it (I2).
  let { status, class: className }: { status: StatusName; class?: string } = $props();

  const current = $derived(statuses[status]);
</script>

<current.icon aria-hidden="true" data-status-icon={current.iconName} class={cn('size-3.5 shrink-0', iconColours[current.tone], className)} />
