<script lang="ts">
  import MaximizeIcon from '@lucide/svelte/icons/maximize';
  import ZoomInIcon from '@lucide/svelte/icons/zoom-in';
  import ZoomOutIcon from '@lucide/svelte/icons/zoom-out';
  import { Panel, useSvelteFlow } from '@xyflow/svelte';
  import IconButton from '#lib/components/icon-button.svelte';

  // The graph's zoom controls as our own icon buttons: Lucide icons, a tooltip naming each, and touch sizes (A4, I1,
  // F4). Svelte Flow's Controls draw their own icons at 26px with native titles. It must sit inside SvelteFlow, whose
  // context useSvelteFlow reads.
  const flow = useSvelteFlow();
</script>

<Panel position="bottom-left" class="flex flex-col gap-1 rounded-lg border border-border bg-card p-1">
  <IconButton label="Zoom in" onclick={() => flow.zoomIn()}><ZoomInIcon /></IconButton>
  <IconButton label="Zoom out" onclick={() => flow.zoomOut()}><ZoomOutIcon /></IconButton>
  <IconButton label="Fit the whole workflow" onclick={() => flow.fitView()}><MaximizeIcon /></IconButton>
</Panel>
