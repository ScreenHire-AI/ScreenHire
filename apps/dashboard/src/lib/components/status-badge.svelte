<script lang="ts">
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { statuses, type StatusName } from '#lib/status.js';
  import { cn } from '#lib/utils.js';

  // A status: its icon, colour and label, always together (T1). `quiet` is for calm states in lists and tables: the icon
  // and muted text, no fill (T3).
  let { status, label, quiet = false, class: className }: { status: StatusName; label: string; quiet?: boolean; class?: string } = $props();

  const tone = $derived(statuses[status].tone);
</script>

<Badge variant={quiet ? 'quiet' : tone} data-status={tone} class={cn('h-6 gap-1.5', !quiet && 'pr-2.5 pl-2', className)}>
  <StatusIcon {status} />
  {label}
</Badge>
