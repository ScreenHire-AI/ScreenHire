<script lang="ts">
  import { useSvelteFlow } from '@xyflow/svelte';
  import { neighbour, type StepNode } from '#lib/workflow-graph.js';

  // The graph's keyboard path (design system section 17, WCAG 2.1.1): on a focused node the arrow keys, Home and End move
  // to another node in reading order, and whatever takes the focus, a node or a button in one, is panned into view.
  // Svelte Flow's own pan on focus covers only the node itself, so it is off and this does both. It must sit inside
  // SvelteFlow, whose context useSvelteFlow reads; `root` is the element around the graph.
  let { root, nodes }: { root: HTMLElement | undefined; nodes: () => readonly StepNode[] } = $props();

  const flow = useSvelteFlow();
  const nodeOf = (target: EventTarget | null) => (target instanceof HTMLElement ? target.closest<HTMLElement>('.svelte-flow__node') : null);

  $effect(() => {
    if (!root) return;
    const element: HTMLElement = root;

    function onkeydown(event: KeyboardEvent) {
      const node = event.target instanceof HTMLElement && event.target.classList.contains('svelte-flow__node') ? event.target : null;
      if (!node?.dataset.id) return;
      const next = neighbour(nodes(), node.dataset.id, event.key);
      if (next === null) return;
      event.preventDefault();
      element.querySelector<HTMLElement>(`.svelte-flow__node[data-id="${CSS.escape(next)}"]`)?.focus();
    }

    function onfocusin(event: FocusEvent) {
      const node = nodeOf(event.target);
      const pane = element.querySelector('.svelte-flow')?.getBoundingClientRect();
      const placed = node?.dataset.id ? flow.getInternalNode(node.dataset.id) : undefined;
      if (!node || !pane || !placed) return;
      const box = node.getBoundingClientRect();
      const inView = box.left >= pane.left && box.right <= pane.right && box.top >= pane.top && box.bottom <= pane.bottom;
      if (inView) return;
      const { x, y } = placed.internals.positionAbsolute;
      void flow.setCenter(x + (placed.measured.width ?? 0) / 2, y + (placed.measured.height ?? 0) / 2, { zoom: flow.getViewport().zoom });
    }

    element.addEventListener('keydown', onkeydown);
    element.addEventListener('focusin', onfocusin);
    return () => {
      element.removeEventListener('keydown', onkeydown);
      element.removeEventListener('focusin', onfocusin);
    };
  });
</script>
