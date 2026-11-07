<script lang="ts">
  import MaximizeIcon from '~icons/lucide/maximize';
  import ZoomInIcon from '~icons/lucide/zoom-in';
  import ZoomOutIcon from '~icons/lucide/zoom-out';
  import { Panel, useSvelteFlow } from '@xyflow/svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import { VIEW_INSET } from '#lib/workflow-graph.js';

  // The graph's zoom controls as our own icon buttons: Lucide icons, a tooltip naming each, and touch sizes (A4, I1,
  // F4). Svelte Flow's Controls draw their own icons at 26px with native titles. It must sit inside SvelteFlow, whose
  // context useSvelteFlow reads.
  const flow = useSvelteFlow();
  // At the top of the canvas, so a canvas taller than the window still has them on the first screen. Fit shows the whole
  // workflow, clear of the toolbar itself, and never bigger than life.
  const fit = () =>
    flow.fitView({ maxZoom: 1, padding: { top: `${VIEW_INSET.top}px`, right: `${VIEW_INSET.right}px`, bottom: `${VIEW_INSET.bottom}px`, left: `${VIEW_INSET.left}px` } });
</script>

<!-- A small card of controls on the canvas, with the surface shadow (section 6). -->
<Panel position="top-left" data-graph-controls class="flex flex-col gap-1 rounded-lg border border-border bg-card p-1 shadow-surface">
  <IconButton label="Zoom in" onclick={() => flow.zoomIn()}><ZoomInIcon /></IconButton>
  <IconButton label="Zoom out" onclick={() => flow.zoomOut()}><ZoomOutIcon /></IconButton>
  <IconButton label="Fit the whole workflow" onclick={fit}><MaximizeIcon /></IconButton>
</Panel>
