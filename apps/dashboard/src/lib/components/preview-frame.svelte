<script lang="ts">
  import MonitorIcon from '~icons/lucide/monitor';
  import SmartphoneIcon from '~icons/lucide/smartphone';
  import TabletIcon from '~icons/lucide/tablet';
  import type { Snippet } from 'svelte';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { cn } from '#lib/utils.js';

  // A candidate page's preview (design system section 11): the result at its real size in a phone, tablet or desktop
  // frame, in an inset. A frame wider than the room scrolls sideways inside the inset, never the page (F1). It opens on
  // Phone; with `fit` it opens on Desktop once the inset has room for a tablet frame, so a phone frame never leaves a
  // blank half of a wide pane. Choosing a size keeps it, whatever the pane does after.
  let {
    device = $bindable(),
    fit = false,
    children,
    class: className,
  }: { device?: 'phone' | 'tablet' | 'desktop'; fit?: boolean; children: Snippet; class?: string } = $props();

  // A tablet frame (768px) and the inset's 16px padding on each side.
  const roomForDesktop = 800;
  let room = $state(0);
  const shown = $derived(device ?? (fit && room >= roomForDesktop ? 'desktop' : 'phone'));

  const devices = [
    { value: 'phone', label: 'Phone', icon: SmartphoneIcon, width: 'w-97.5' },
    { value: 'tablet', label: 'Tablet', icon: TabletIcon, width: 'w-192' },
    { value: 'desktop', label: 'Desktop', icon: MonitorIcon, width: 'w-full' },
  ] as const;
  const frame = $derived(devices.find((option) => option.value === shown) ?? devices[0]);
</script>

<div class={cn('flex min-w-0 flex-col gap-3', className)}>
  <div role="group" aria-label="Screen size" class="flex gap-1 overflow-x-auto">
    {#each devices as option (option.value)}
      {@const Icon = option.icon}
      <Toggle bind:pressed={() => shown === option.value, () => (device = option.value)}><Icon aria-hidden="true" />{option.label}</Toggle>
    {/each}
  </div>
  <div bind:clientWidth={room} class="overflow-x-auto rounded-lg bg-secondary p-4">
    <div data-preview-device={frame.value} class={cn('mx-auto max-w-none shrink-0 overflow-hidden rounded-panel border border-border bg-card shadow-surface', frame.width)}>
      {@render children()}
    </div>
  </div>
</div>
