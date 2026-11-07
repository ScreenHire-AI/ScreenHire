<script lang="ts" module>
  /** What pressing a node does; the graph passes them only when the workflow can be edited on this screen. */
  export interface GraphActions {
    /** Opens the automation's form at the part `path` names (its trigger, conditions or an action). */
    edit: (automation: number, path: string) => void;
    /** Opens the drawer of a stage's automations, or the whole workflow's (null). */
    openLane: (stageId: string | null) => void;
    /** Adds an automation to a stage's lane, or the whole workflow's (null), and opens its form. */
    add: (stageId: string | null) => void;
    /** Adds a first condition to the automation `automationId` and opens its form there. */
    addCondition: (automationId: string) => void;
    /** Adds an action to the end of the automation `automationId` and opens its form there. */
    addAction: (automationId: string) => void;
    /** Points the "move to a stage" action node `source` at the stage node `target` (a link drawn with the pointer). */
    connect: (source: string, target: string) => void;
  }
</script>

<script lang="ts">
  import LayersIcon from '~icons/lucide/layers';
  import MilestoneIcon from '~icons/lucide/milestone';
  import PlayIcon from '~icons/lucide/play';
  import PlusIcon from '~icons/lucide/plus';
  import SplitIcon from '~icons/lucide/split';
  import ZapIcon from '~icons/lucide/zap';
  import { Handle, Position, type NodeProps } from '@xyflow/svelte';
  import { mergeProps } from 'bits-ui';
  import ProblemIcon from '#lib/components/problem-icon.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { isAdd, type StepNode } from '#lib/workflow-graph.js';
  import { usePathLight } from './graph-light.svelte.js';

  // One node of the workflow graph. Each kind has its own look and icon, so what it is reads before its text does:
  // When (a trigger), If (conditions), Then (an action), and the lane or stage the automations hang from. With
  // `actions` the whole node is one button that opens what it stands for; without them (a version that cannot be
  // edited, a phone) it is a focusable box that reads the same. Either way it is one tab stop, and the arrow keys
  // move between nodes (GraphKeys). The text is clamped so the node has a fixed height (`nodeHeight`), with the full
  // value in a tooltip. Hover and focus light the node's path, and a path that is not lit is quietened by its edge
  // and shadow alone: the text keeps its contrast.
  let { id, data, isConnectable }: NodeProps<StepNode> = $props();

  const light = usePathLight();
  const state = $derived(light.node(id));
  const actions = $derived(data.actions as GraphActions | undefined);
  // Only a "move to a stage" action draws a link, and only to a stage: the pointer shortcut for the action's Stage field.
  const linksOut = $derived(Boolean(data.movesToStage) && isConnectable);
  const linksIn = $derived(data.kind === 'stage' && isConnectable);

  const heading = { lane: 'Lane', stage: 'Stage', trigger: 'When', conditions: 'If', action: 'Then' } as const;
  const icons = { lane: LayersIcon, stage: MilestoneIcon, trigger: ZapIcon, conditions: SplitIcon, action: PlayIcon } as const;
  const spoken = $derived(
    data.kind === 'action'
      ? `action ${Number(/actions\[(\d+)\]/.exec(data.path)?.[1]) + 1} in ${data.automationName}`
      : data.kind === 'conditions'
        ? `in ${data.automationName}`
        : '',
  );
  const head = $derived(data.kind in heading ? (data.kind as keyof typeof heading) : null);
  const Icon = $derived(head ? icons[head] : null);

  function press() {
    if (!actions) return;
    if (data.kind === 'lane' || data.kind === 'stage') actions.openLane(data.lane);
    else if (data.kind === 'add-automation') actions.add(data.lane);
    else if (data.kind === 'add-condition') actions.addCondition(data.automationId!);
    else if (data.kind === 'add-action') actions.addAction(data.automationId!);
    else if (data.automation !== null) actions.edit(data.automation, data.path);
  }

  // Pressable or not, a node is a tab stop, so the keyboard always gets the focus ring, in the node's own shape. A
  // button gets the hand from the theme (A10).
  const frame = $derived([
    'relative flex size-full text-left text-caption text-card-foreground outline-none',
    isAdd(data.kind) ? 'rounded-full' : 'rounded-lg',
    'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background',
  ]);
  const surface = $derived([
    'bg-card shadow-surface motion-safe:transition-[border-color,box-shadow]',
    data.kind === 'conditions' && 'bg-secondary',
    // Lit: the edge says so. Quiet: the edge and the shadow soften; the text does not.
    state === 'lit' ? 'border-foreground' : state === 'quiet' ? 'border-border-subtle shadow-none' : data.kind === 'lane' || data.kind === 'stage' ? 'border-foreground/40' : 'border-border',
    data.problems.length > 0 && 'border-status-verify-mark',
    // An automation that is off keeps full-contrast text (X1): a dashed edge and ", off" in its label say so.
    (data.disabled || data.kind === 'lane') && 'border-dashed',
    actions && 'group-hover/node:border-foreground group-hover/node:shadow-raised',
  ]);
</script>

<Handle type="target" id="top" position={Position.Top} isConnectable={linksIn} class={linksIn ? '!h-2 !w-12 !opacity-0' : '!size-px !opacity-0'} />
<Handle type="target" id="left" position={Position.Left} isConnectable={false} class="!size-px !opacity-0" />
<div class="group/node size-full" data-node-kind={data.kind} data-state={state}>
  {#if isAdd(data.kind)}
    {#if actions}
      <!-- A plus is icon only, so its name is also its tooltip (A4). -->
      <Tooltip.Root>
        <Tooltip.Trigger>
          {#snippet child({ props })}
            <button {...mergeProps(props, { onclick: press })} type="button" class={[frame, 'items-center justify-center']} aria-label={data.label}>
              <span
                class={[
                  'flex size-full items-center justify-center rounded-full border bg-card text-muted-foreground shadow-surface motion-safe:transition-[border-color,box-shadow,color]',
                  state === 'lit' ? 'border-foreground' : 'border-border',
                  'group-hover/node:border-foreground group-hover/node:text-foreground group-hover/node:shadow-raised',
                ]}
              >
                <PlusIcon aria-hidden="true" class="size-4" />
              </span>
            </button>
          {/snippet}
        </Tooltip.Trigger>
        <Tooltip.Content>{data.label}</Tooltip.Content>
      </Tooltip.Root>
    {/if}
  {:else}
    {#snippet body()}
      <span class={['flex size-full min-w-0 flex-col gap-1 rounded-lg border px-2.5 py-2', surface]}>
        <span class="flex items-center gap-1.5 text-muted-foreground">
          {#if Icon}<Icon aria-hidden="true" class="size-3.5 shrink-0" />{/if}
          <!-- A button's name is its own text (WCAG 2.5.3), so what the position adds, which automation and which action,
               is read here, after the word the eye sees. -->
          <span>{data.endStage ? 'End stage' : head ? heading[head] : ''}{data.disabled ? ', off' : ''}{#if spoken}{' '}<span class="sr-only">{spoken}</span>{/if}</span>
        </span>
        <TruncatedText text={data.title} lines={2} tabindex={-1} class="font-semibold" />
        <span class="block min-h-4 min-w-0">{#if data.detail}<TruncatedText text={data.detail} tabindex={-1} class="text-muted-foreground" />{/if}</span>
        {#if data.problems.length > 0}
          <span class="flex min-w-0 items-baseline gap-1.5 font-semibold text-status-verify">
            <ProblemIcon class="size-3.5 self-center" /><TruncatedText text={data.problems.join(' ')} lines={2} tabindex={-1} class="flex-1" />
          </span>
        {/if}
      </span>
    {/snippet}
    {#if actions}
      <button type="button" class={frame} onclick={press}>{@render body()}</button>
    {:else}
      <!-- A box that cannot be pressed is still one tab stop, so a keyboard user can read the graph node by node (design system section 17). -->
      <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
      <div class={frame} role="group" aria-label={data.label} tabindex="0">{@render body()}</div>
    {/if}
  {/if}
</div>
<Handle type="source" id="bottom" position={Position.Bottom} isConnectable={false} class="!size-px !opacity-0" />
<Handle type="source" id="right" position={Position.Right} isConnectable={linksOut} class={linksOut ? '!size-2.5 !border-foreground !bg-card' : '!size-px !opacity-0'} />
