<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { Background, SvelteFlow, type Edge } from '@xyflow/svelte';
  import { untrack } from 'svelte';
  import '@xyflow/svelte/dist/style.css';
  import { preferences } from '#lib/preferences.svelte.js';
  import { emailTemplateNames, errorsUnder } from '#lib/workflow.js';
  import { toGraph, type StepNode } from '#lib/workflow-graph.js';
  import GraphControls from './graph-controls.svelte';
  import StepNodeView from './step-node.svelte';

  // The power-user view (workflow-engine.md §4): the same workflow as a graph. A projection only: it is rebuilt from the
  // workflow on every change, nothing in it is stored, and edits go through the automation forms (Edit on a node).
  // Nothing needs dragging: the wheel or trackpad pans (panOnScroll), the controls zoom and fit, and the pipeline view
  // shows the same workflow as a list (WCAG 2.5.7). Without `onedit` (a version that cannot be edited) nodes have no
  // Edit button. It fills a flush panel.
  let { workflow, errors, onedit }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; onedit?: (automation: number) => void } = $props();

  const nodeTypes = { step: StepNodeView };
  // The organization's template names, for "Send the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));

  function project() {
    const graph = toGraph(workflow, templateNames);
    return {
      nodes: graph.nodes.map((node) => ({ ...node, data: { ...node.data, invalid: errorsUnder(errors, node.data.path).length > 0, onedit } })),
      edges: graph.edges,
    };
  }

  // Built before the first render, so fitView frames the real graph; rebuilt on every change after that. Svelte Flow
  // binds the arrays (it writes measured sizes back), so they are state rather than derived.
  const initial = untrack(project);
  let nodes = $state.raw<StepNode[]>(initial.nodes);
  let edges = $state.raw<Edge[]>(initial.edges);

  $effect(() => {
    const next = project();
    untrack(() => {
      nodes = next.nodes;
      edges = next.edges;
    });
  });
</script>

<div class="h-[560px] overflow-hidden rounded-b-panel">
  <SvelteFlow
    bind:nodes
    bind:edges
    {nodeTypes}
    fitView
    colorMode={preferences.theme}
    panOnScroll
    nodesDraggable={false}
    nodesConnectable={false}
    nodesFocusable={false}
    edgesFocusable={false}
    elementsSelectable={false}
    minZoom={0.3}
  >
    <Background />
    <GraphControls />
  </SvelteFlow>
</div>
