<script lang="ts">
  import { EdgeLabel, type EdgeProps } from '@xyflow/svelte';
  import { arrowhead, routePath, type RouteEdge } from '#lib/workflow-graph.js';
  import { usePathLight } from './graph-light.svelte.js';

  // A line of the workflow graph. The path was worked out with the layout (`toGraph`), so it runs where the layout
  // reserved room for it and never through a node; this only draws it: rounded corners, an arrowhead at the end (the
  // flow runs one way) and, on a move, the words "Moves to". Svelte Flow's own markers live in a shared definition away
  // from the line, so they cannot take its lit or quiet colour; the head is drawn here and takes `currentColor` with the
  // line. Lit and quiet come from the path being lit (`PathLight`).
  let { id, data }: EdgeProps<RouteEdge> = $props();

  const light = usePathLight();
  const state = $derived(light.edge(id));
  const colour = $derived(state === 'lit' ? 'text-foreground' : state === 'quiet' ? 'text-muted-foreground/60' : 'text-muted-foreground');
</script>

<g data-edge={id} data-state={state} class={[colour, 'motion-safe:transition-colors']}>
  <path
    d={routePath(data!.points, data!.arrow)}
    fill="none"
    stroke="currentColor"
    stroke-width={state === 'lit' ? 2 : 1.5}
    stroke-dasharray={data!.dashed ? '4 4' : undefined}
    stroke-linejoin="round"
  />
  {#if data!.arrow}<path d={arrowhead(data!.points)} fill="currentColor" stroke="currentColor" stroke-width="1" stroke-linejoin="round" />{/if}
</g>
{#if data!.label && data!.labelAt}
  <EdgeLabel
    x={data!.labelAt.x}
    y={data!.labelAt.y}
    class={[
      'rounded-full border bg-card px-2 text-caption text-card-foreground',
      state === 'lit' ? 'border-foreground' : state === 'quiet' ? 'border-border-subtle' : 'border-border',
    ]}
  >
    {data!.label}
  </EdgeLabel>
{/if}
