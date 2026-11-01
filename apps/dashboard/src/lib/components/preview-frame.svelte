<script lang="ts">
  import MonitorIcon from '~icons/lucide/monitor';
  import SmartphoneIcon from '~icons/lucide/smartphone';
  import TabletIcon from '~icons/lucide/tablet';
  import type { Snippet } from 'svelte';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { cn } from '#lib/utils.js';

  // A candidate page's preview (design system section 11): the result at its real size in a phone, tablet or desktop
  // frame, in an inset. A frame wider than the room scrolls sideways inside the inset, never the page (F1).
  let { device = $bindable('phone'), children, class: className }: { device?: 'phone' | 'tablet' | 'desktop'; children: Snippet; class?: string } = $props();

  const devices = [
    { value: 'phone', label: 'Phone', icon: SmartphoneIcon, width: 'w-97.5' },
    { value: 'tablet', label: 'Tablet', icon: TabletIcon, width: 'w-192' },
    { value: 'desktop', label: 'Desktop', icon: MonitorIcon, width: 'w-full' },
  ] as const;
  const frame = $derived(devices.find((option) => option.value === device) ?? devices[0]);
</script>

<div class={cn('flex min-w-0 flex-col gap-3', className)}>
  <div role="group" aria-label="Screen size" class="flex gap-1 overflow-x-auto">
    {#each devices as option (option.value)}
      {@const Icon = option.icon}
      <Toggle bind:pressed={() => device === option.value, () => (device = option.value)}><Icon aria-hidden="true" />{option.label}</Toggle>
    {/each}
  </div>
  <div class="overflow-x-auto rounded-lg bg-secondary p-4">
    <div data-preview-device={frame.value} class={cn('mx-auto max-w-none shrink-0 overflow-hidden rounded-panel border border-border bg-card shadow-surface', frame.width)}>
      {@render children()}
    </div>
  </div>
</div>
