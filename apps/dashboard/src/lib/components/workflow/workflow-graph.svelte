<script lang="ts">
  import type { DefinitionError, EvaluationOutcome, HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { Background, Panel, SvelteFlow, type Edge } from '@xyflow/svelte';
  import { untrack } from 'svelte';
  import { MediaQuery } from 'svelte/reactivity';
  import '@xyflow/svelte/dist/style.css';
  import { Button } from '#lib/components/ui/button/index.js';
  import { IsMobile } from '#lib/hooks/is-mobile.svelte.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { emailTemplateNames } from '#lib/workflow.js';
  import { FIRST_VIEW_MIN_ZOOM, VIEW_INSET, canConnect, pathOf, testedPath, toGraph, type StepNode } from '#lib/workflow-graph.js';
  import GraphControls from './graph-controls.svelte';
  import GraphFit from './graph-fit.svelte';
  import GraphKeys from './graph-keys.svelte';
  import { providePathLight } from './graph-light.svelte.js';
  import RoutedEdge from './routed-edge.svelte';
  import StepNodeView, { type GraphActions } from './step-node.svelte';

  // The power-user view (workflow-engine.md §4): the same workflow as a graph. A projection only: it is rebuilt from the
  // workflow on every change and nothing in it is stored. The stages run left to right and each stage's automations
  // hang beneath it, read down as When, If, Then; every line has an arrowhead and the flow runs one way. With `actions`
  // (the builder) a node is a button that opens what it stands for, and a plus where a condition, an action or an
  // automation would go adds one. Hovering or focusing a node lights its path; `tested` lights the path a test took.
  // Every node is a tab stop, and the arrow keys move between nodes (GraphKeys). Nothing needs dragging: the wheel or
  // trackpad pans (panOnScroll), the controls zoom and fit, and a link drawn from a "move to a stage" action to a stage
  // is only a shortcut for the action's Stage field (WCAG 2.5.7). On a phone the graph is for reading; editing stays in
  // the Pipeline view. It fills a flush panel.
  let {
    workflow,
    errors,
    actions,
    tested,
    onclear,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    actions?: GraphActions;
    /** The outcome of each automation in the last test, whose path is lit while nothing is hovered or focused. */
    tested?: ReadonlyMap<string, EvaluationOutcome>;
    /** Drops the test's path from the graph. */
    onclear?: () => void;
  } = $props();

  const nodeTypes = { step: StepNodeView };
  const edgeTypes = { routed: RoutedEdge };
  // The organization's template names, for "Send the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const phone = new IsMobile();
  const touch = new MediaQuery('pointer: coarse');
  const editing = $derived(phone.current ? undefined : actions);
  const empty = $derived(workflow.automations.length === 0);
  const graph = $derived(toGraph(workflow, { templateNames, errors, editable: Boolean(editing), touch: touch.current }));
  // It starts at the top left at a readable size until GraphFit has the pane's size to fit the workflow to.
  const initialViewport = { x: VIEW_INSET.left, y: VIEW_INSET.top, zoom: FIRST_VIEW_MIN_ZOOM };

  // Which path is lit: the node under the pointer, else the one with the focus, else the path a test took.
  const light = providePathLight();
  let hovered = $state<string | null>(null);
  let focused = $state<string | null>(null);
  const lit = $derived.by(() => {
    const active = hovered ?? focused;
    if (active) return pathOf(graph, active);
    return tested && tested.size > 0 ? testedPath(graph, tested) : null;
  });
  $effect(() => light.set(lit));

  // Svelte Flow binds the arrays (it writes measured sizes back), so they are state rather than derived.
  let nodes = $state.raw<StepNode[]>([]);
  let edges = $state.raw<Edge[]>([]);
  let root = $state<HTMLElement>();

  const sameNode = (a: StepNode, b: StepNode) =>
    a.position.x === b.position.x &&
    a.position.y === b.position.y &&
    a.height === b.height &&
    a.data.actions === b.data.actions &&
    JSON.stringify({ ...a.data, actions: null }) === JSON.stringify({ ...b.data, actions: null });

  function project() {
    // Unchanged nodes keep their object, so a keyboard user's focus and Svelte Flow's measurements survive a change.
    const previous = new Map(untrack(() => nodes).map((node) => [node.id, node]));
    return {
      nodes: graph.nodes.map((node) => {
        const old = previous.get(node.id);
        const next: StepNode = { ...node, data: { ...node.data, actions: editing }, ...(old?.measured && { measured: old.measured }) };
        return old && sameNode(old, next) ? old : next;
      }),
      edges: graph.edges as Edge[],
    };
  }

  // Built before the first render; rebuilt on every change after that.
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

  const nodeOf = (target: EventTarget | null) => (target instanceof HTMLElement ? target.closest<HTMLElement>('.svelte-flow__node')?.dataset.id : undefined);
</script>

{#if actions && phone.current}
  <p class="border-b border-border-subtle px-4 py-2.5 text-muted-foreground">On a phone the graph is for reading. Edit in Pipeline.</p>
{/if}
{#if tested && tested.size > 0 && onclear}
  <div class="flex flex-wrap items-center justify-between gap-2 border-b border-border-subtle px-4 py-2">
    <p>Showing the path of your last test.</p>
    <Button variant="outline" size="sm" onclick={onclear}>Clear</Button>
  </div>
{/if}
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  bind:this={root}
  class="h-[max(560px,70svh)] overflow-hidden rounded-b-panel"
  onfocusin={(event) => (focused = nodeOf(event.target) ?? null)}
  onfocusout={() => (focused = null)}
>
  <SvelteFlow
    bind:nodes
    bind:edges
    {nodeTypes}
    {edgeTypes}
    {initialViewport}
    proOptions={{ hideAttribution: true }}
    colorMode={preferences.theme}
    panOnScroll
    nodesDraggable={false}
    nodesConnectable={Boolean(editing)}
    nodesFocusable={false}
    edgesFocusable={false}
    elementsSelectable={false}
    autoPanOnNodeFocus={false}
    disableKeyboardA11y
    deleteKey={null}
    ariaLabelConfig={{ 'handle.ariaLabel': 'Link point' }}
    isValidConnection={(connection) => canConnect(workflow, connection.source, connection.target)}
    onbeforeconnect={(connection) => {
      editing?.connect(connection.source, connection.target);
      return false;
    }}
    onnodepointerenter={({ node }) => (hovered = node.id)}
    onnodepointerleave={() => (hovered = null)}
    minZoom={0.3}
  >
    <Background />
    <GraphControls />
    <GraphKeys {root} nodes={() => nodes} />
    <GraphFit {root} bounds={() => graph.bounds} />
    {#if empty}
      <Panel position="bottom-center" class="mb-[12%] max-w-sm rounded-lg bg-secondary p-4">
        <p class="font-semibold">No automations yet</p>
        <p class="mt-1 text-muted-foreground">An automation does something on its own when an application arrives or reaches a stage, such as adding a tag or telling the team.</p>
        {#if editing}
          <Button size="lg" class="mt-3" onclick={() => editing?.add(null)}>Add an automation</Button>
        {/if}
      </Panel>
    {/if}
  </SvelteFlow>
</div>
