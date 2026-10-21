<script lang="ts" module>
  /** The status meanings in design system section 3.2. `ai` is information and has no mark. */
  export type Status = 'clear' | 'review' | 'verify' | 'ai';

  const shapes = { clear: 'circle', review: 'diamond', verify: 'square' } as const;
</script>

<script lang="ts">
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { cn } from '#lib/utils.js';

  // A status: shape mark, colour and label, always together (T1). `quiet` is for calm states in lists and tables: the
  // mark and muted text, no fill (T3).
  let { status, label, quiet = false, class: className }: { status: Status; label: string; quiet?: boolean; class?: string } = $props();
</script>

<Badge variant={quiet ? 'quiet' : status} data-status={status} class={cn('h-[22px] gap-1.5', !quiet && 'px-2.5', className)}>
  {#if status !== 'ai'}
    <StatusMark shape={shapes[status]} tone={status} class="size-1.5" />
  {/if}
  {label}
</Badge>
