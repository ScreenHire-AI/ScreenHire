<script lang="ts">
  import { useSvelteFlow } from '@xyflow/svelte';
  import { neighbour, type StepNode } from '#lib/workflow-graph.js';

  // The graph's keyboard path (design system section 17, WCAG 2.1.1): on a focused node the arrow keys, Home and End move
  // to another node in reading order, and whatever takes the focus, a node or a button in one, is panned into view and
  // out from under the zoom toolbar.
  // Svelte Flow's own pan on focus covers only the node itself, so it is off and this does both. It must sit inside
  // SvelteFlow, whose context useSvelteFlow reads; `root` is the element around the graph. The browser scrolls a clipped
  // box to show what Tab focuses, which would shift the canvas under Svelte Flow's own pan, so that scroll is undone and
  // the pan does the showing.
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

    function unscroll(from: EventTarget | null) {
      for (let box = from instanceof HTMLElement ? from : null; box && box !== element.parentElement; box = box.parentElement) {
        if (box.scrollLeft !== 0) box.scrollLeft = 0;
        if (box.scrollTop !== 0) box.scrollTop = 0;
      }
    }

    function onscroll(event: Event) {
      if (event.target instanceof HTMLElement && element.contains(event.target)) unscroll(event.target);
    }

    function onfocusin(event: FocusEvent) {
      unscroll(event.target instanceof HTMLElement ? event.target.parentElement : null);
      const node = nodeOf(event.target);
      const pane = element.querySelector('.svelte-flow')?.getBoundingClientRect();
      const placed = node?.dataset.id ? flow.getInternalNode(node.dataset.id) : undefined;
      if (!node || !pane || !placed) return;
      const box = node.getBoundingClientRect();
      // The zoom toolbar floats over the canvas, so a node under it is not in view either (WCAG 2.4.11).
      const toolbar = element.querySelector('[data-graph-controls]')?.getBoundingClientRect();
      const covered = toolbar && box.left < toolbar.right && box.right > toolbar.left && box.top < toolbar.bottom && box.bottom > toolbar.top;
      const inView = box.left >= pane.left && box.right <= pane.right && box.top >= pane.top && box.bottom <= pane.bottom && !covered;
      if (inView) return;
      const { x, y } = placed.internals.positionAbsolute;
      void flow.setCenter(x + (placed.measured.width ?? 0) / 2, y + (placed.measured.height ?? 0) / 2, { zoom: flow.getViewport().zoom });
    }

    element.addEventListener('keydown', onkeydown);
    element.addEventListener('focusin', onfocusin);
    element.addEventListener('scroll', onscroll, true);
    return () => {
      element.removeEventListener('scroll', onscroll, true);
      element.removeEventListener('keydown', onkeydown);
      element.removeEventListener('focusin', onfocusin);
    };
  });
</script>
