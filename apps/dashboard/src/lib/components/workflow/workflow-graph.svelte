<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { Background, Controls, SvelteFlow, type Edge } from '@xyflow/svelte';
  import '@xyflow/svelte/dist/style.css';
  import { errorsUnder } from '#lib/workflow.js';
  import { toGraph, type StepNode } from '#lib/workflow-graph.js';
  import StepNodeView from './step-node.svelte';

  // The power-user view (workflow-engine.md §4): the same workflow as a graph. A projection only: it is rebuilt from the
  // workflow on every change, nothing in it is stored, and edits go through the automation forms (Edit on a node).
  let { workflow, errors, onedit }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; onedit: (automation: number) => void } = $props();

  const nodeTypes = { step: StepNodeView };

  let nodes = $state.raw<StepNode[]>([]);
  let edges = $state.raw<Edge[]>([]);

  $effect(() => {
    const graph = toGraph(workflow);
    nodes = graph.nodes.map((node) => ({ ...node, data: { ...node.data, invalid: errorsUnder(errors, node.data.path).length > 0, onedit } }));
    edges = graph.edges;
  });
</script>

<div class="h-[560px] overflow-hidden rounded-xl border border-border bg-card">
  <SvelteFlow
    bind:nodes
    bind:edges
    {nodeTypes}
    fitView
    nodesDraggable={false}
    nodesConnectable={false}
    nodesFocusable={false}
    edgesFocusable={false}
    elementsSelectable={false}
    minZoom={0.3}
  >
    <Background />
    <Controls showLock={false} />
  </SvelteFlow>
</div>
