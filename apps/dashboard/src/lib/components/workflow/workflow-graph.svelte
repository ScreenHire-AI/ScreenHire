<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { Background, SvelteFlow, type Edge } from '@xyflow/svelte';
  import { untrack } from 'svelte';
  import { MediaQuery } from 'svelte/reactivity';
  import '@xyflow/svelte/dist/style.css';
  import { IsMobile } from '#lib/hooks/is-mobile.svelte.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { emailTemplateNames } from '#lib/workflow.js';
  import { canConnect, toGraph, type StepNode } from '#lib/workflow-graph.js';
  import GraphControls from './graph-controls.svelte';
  import GraphKeys from './graph-keys.svelte';
  import StepNodeView, { type GraphActions } from './step-node.svelte';

  // The power-user view (workflow-engine.md §4): the same workflow as a graph. A projection only: it is rebuilt from the
  // workflow on every change and nothing in it is stored. With `actions` (the builder) its nodes edit the workflow: a
  // lane or stage adds an automation, an automation's nodes open its form, the one the Pipeline view's drawer shows, and
  // a trigger node removes it. Every node is a tab stop, and the arrow keys move between nodes (GraphKeys). Nothing needs
  // dragging: the wheel or trackpad pans (panOnScroll), the controls zoom and fit, and a link drawn from a "move to a
  // stage" action to a stage is only a shortcut for the action's Stage field (WCAG 2.5.7). On a phone the graph is for
  // reading; editing stays in the Pipeline view. It fills a flush panel.
  let { workflow, errors, actions }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; actions?: GraphActions } = $props();

  const nodeTypes = { step: StepNodeView };
  // The organization's template names, for "Send the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const phone = new IsMobile();
  const touch = new MediaQuery('pointer: coarse');
  const editing = $derived(phone.current ? undefined : actions);

  // Svelte Flow binds the arrays (it writes measured sizes back), so they are state rather than derived.
  let nodes = $state.raw<StepNode[]>([]);
  let edges = $state.raw<Edge[]>([]);
  let root = $state<HTMLElement>();

  const sameNode = (a: StepNode, b: StepNode) =>
    a.position.x === b.position.x &&
    a.position.y === b.position.y &&
    a.ariaLabel === b.ariaLabel &&
    a.data.actions === b.data.actions &&
    JSON.stringify({ ...a.data, actions: null }) === JSON.stringify({ ...b.data, actions: null });

  function project() {
    const graph = toGraph(workflow, { templateNames, errors, editable: Boolean(editing), touch: touch.current });
    // A node rebuilt without its measured size is hidden until Svelte Flow measures it again, which would take the focus
    // away from a keyboard user on every change. Unchanged nodes keep their object, changed ones their size.
    const previous = new Map(untrack(() => nodes).map((node) => [node.id, node]));
    return {
      nodes: graph.nodes.map((node) => {
        const old = previous.get(node.id);
        const next: StepNode = { ...node, data: { ...node.data, actions: editing }, ...(old?.measured && { measured: old.measured }) };
        return old && sameNode(old, next) ? old : next;
      }),
      edges: graph.edges,
    };
  }

  // Built before the first render, so fitView frames the real graph; rebuilt on every change after that.
  const initial = untrack(project);
  nodes = initial.nodes;
  edges = initial.edges;

  $effect(() => {
    const next = project();
    untrack(() => {
      nodes = next.nodes;
      edges = next.edges;
    });
  });
</script>

{#if actions && phone.current}
  <p class="border-b border-border-subtle px-4 py-2.5 text-muted-foreground">On a phone the graph is for reading. Edit in Pipeline.</p>
{/if}
<div bind:this={root} class="h-[560px] overflow-hidden rounded-b-panel">
  <SvelteFlow
    bind:nodes
    bind:edges
    {nodeTypes}
    fitView
    colorMode={preferences.theme}
    panOnScroll
    nodesDraggable={false}
    nodesConnectable={Boolean(editing)}
    nodesFocusable
    edgesFocusable={false}
    elementsSelectable={false}
    autoPanOnNodeFocus={false}
    deleteKey={null}
    ariaLabelConfig={{
      'node.a11yDescription.keyboardDisabled': 'Arrow keys move to the next node. Tab reaches the buttons in this one.',
      'handle.ariaLabel': 'Link point',
    }}
    isValidConnection={(connection) => canConnect(workflow, connection.source, connection.target)}
    onbeforeconnect={(connection) => {
      editing?.connect(connection.source, connection.target);
      return false;
    }}
    minZoom={0.3}
  >
    <Background />
    <GraphControls />
    <GraphKeys {root} nodes={() => nodes} />
  </SvelteFlow>
</div>
