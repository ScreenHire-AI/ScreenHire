<script lang="ts">
  import { useSvelteFlow } from '@xyflow/svelte';
  import { untrack } from 'svelte';
  import { firstView } from '#lib/workflow-graph.js';

  // The graph's first view (`firstView`): the whole workflow when it fits at a readable zoom, otherwise the top left at
  // that zoom. Set once, when the pane has a size to fit to; after that the person's pan and zoom are theirs. It must sit
  // inside SvelteFlow, whose context useSvelteFlow reads; `root` is the element around the graph.
  let { root, bounds }: { root: HTMLElement | undefined; bounds: () => { width: number; height: number } } = $props();

  const flow = useSvelteFlow();
  let done = false;

  $effect(() => {
    if (!root || done) return;
    const pane = root.getBoundingClientRect();
    if (pane.width === 0 || pane.height === 0) return;
    done = true;
    void flow.setViewport(firstView(untrack(bounds), pane));
  });
</script>
