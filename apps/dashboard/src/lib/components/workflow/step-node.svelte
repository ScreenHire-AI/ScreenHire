<script lang="ts" module>
  import type { ChoiceBlank } from '#lib/automation-sentence.js';

  /** What pressing a node does; the graph passes them only when the workflow can be edited on this screen. */
  export interface GraphActions {
    /** Opens the automation's form at the part `path` names (its trigger, conditions or an action). */
    edit: (automation: number, path: string) => void;
    /** Opens the drawer of a stage's automations, or Any stage's (null). */
    openLane: (stageId: string | null) => void;
    /**
     * Opens the recipes for a stage's band, or every recipe (null). `anyStage` is the Any stage band's own control, whose
     * Write your own stays in that band; the empty map's button passes neither.
     */
    add: (stageId: string | null, anyStage?: boolean) => void;
    /** What a plus offers to add to the automation `automationId`: the facts a condition can check, or the kinds of step. */
    offers: (part: 'condition' | 'step', automationId: string) => ChoiceBlank | null;
    /** Adds the condition or step chosen from `offers` and opens the automation at the first thing it still asks for. */
    addPart: (part: 'condition' | 'step', automationId: string, choice: string) => void;
    /** Removes what the node stands for, offering Undo. */
    remove: (nodeId: string) => void;
    /** Points the "move to a stage" action node `source` at the stage node `target` (a link drawn with the pointer). */
    connect: (source: string, target: string) => void;
  }
</script>

<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import Trash2Icon from '~icons/lucide/trash-2';
  import { Handle, Position, type NodeProps } from '@xyflow/svelte';
  import { mergeProps } from 'bits-ui';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import ProblemIcon from '#lib/components/problem-icon.svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Kbd } from '#lib/components/ui/kbd/index.js';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { LANE_BAND, isAdd, type StepNode } from '#lib/workflow-graph.js';
  import ChoiceList from './choice-list.svelte';
  import { usePathLight } from './graph-light.svelte.js';

  // One node of the workflow graph. Each kind has its own look and its word, so what it is reads before its text does:
  // When (a trigger), If (conditions), Then (an action), and the lane or stage the automations hang from. A node has no
  // icon: the word is the label, and the text under it is short and plain. With
  // `actions` the whole node is one button that opens what it stands for; without them (a version that cannot be
  // edited, a phone) it is a focusable box that reads the same. Beside the button, never inside it, is the node's "..." menu,
  // which takes away what the node stands for (A18): the node is one tab stop and its menu the next, and the arrow keys
  // move between nodes (GraphKeys). A stage's add button says New automation in words; a plus is small, and opens a list of
  // what it can add, so nothing is added until it is chosen. The text is clamped so the node has a fixed height (`nodeHeight`), with the full
  // value in a tooltip. Hover and focus light the node's path, and a path that is not lit is quietened by its edge
  // and shadow alone: the text keeps its contrast.
  let { id, data, isConnectable }: NodeProps<StepNode> = $props();

  const light = usePathLight();
  const shade = $derived(light.node(id));
  const actions = $derived(data.actions as GraphActions | undefined);
  // Only a "move to a stage" action draws a link, and only to a stage: the pointer shortcut for the action's Stage field.
  const linksOut = $derived(Boolean(data.movesToStage) && isConnectable);
  const linksIn = $derived(data.kind === 'stage' && isConnectable);

  const heading = { lane: 'Lane', stage: 'Stage', trigger: 'When', conditions: 'If', action: 'Then' } as const;
  // What the position adds for a screen reader: a trigger names its automation (its visible detail), the others say which.
  const spoken = $derived(
    data.kind === 'trigger'
      ? data.automationName
      : data.kind === 'action'
        ? `action ${Number(/actions\[(\d+)\]/.exec(data.path)?.[1]) + 1} in ${data.automationName}`
        : data.kind === 'conditions'
          ? `in ${data.automationName}`
          : '',
  );
  const head = $derived(data.kind in heading ? (data.kind as keyof typeof heading) : null);

  // Not "When X" or "Then step 1 in X", which name the node itself: the menu's name is its own.
  const menuLabel = $derived(
    data.kind === 'trigger'
      ? `More for ${data.automationName || 'this automation'}`
      : data.kind === 'conditions'
        ? `More for the conditions of ${data.automationName || 'this automation'}`
        : `More for step ${Number(/actions\[(\d+)\]/.exec(data.path)?.[1]) + 1} of ${data.automationName || 'this automation'}`,
  );

  const part = $derived(data.kind === 'add-condition' ? ('condition' as const) : ('step' as const));
  const uid = $props.id();
  let adding = $state(false);
  let chosen = false;
  const offer = $derived(adding && data.automationId ? (actions?.offers(part, data.automationId) ?? null) : null);
  function add(choice: string) {
    chosen = true;
    adding = false;
    actions?.addPart(part, data.automationId!, choice);
  }

  // The removal waits for the menu to close: the focus is then on its button, which leaves with the node, so the Undo notice
  // takes it.
  let removing = false;
  function removeOnceClosed() {
    if (removing) actions?.remove(id);
    removing = false;
  }

  function press() {
    if (!actions) return;
    if (data.kind === 'lane' || data.kind === 'stage') actions.openLane(data.lane);
    else if (data.kind === 'add-automation') actions.add(data.lane, data.band === LANE_BAND);
    else if (data.automation !== null) actions.edit(data.automation, data.path);
  }

  // Pressable or not, a node is a tab stop, so the keyboard always gets the focus ring, in the node's own shape. A
  // button gets the hand from the theme (A10).
  const frame = $derived([
    'relative flex size-full text-left text-caption text-card-foreground outline-none',
    'rounded-lg',
    'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background',
  ]);
  const surface = $derived([
    'bg-card shadow-surface motion-safe:transition-[border-color,box-shadow]',
    data.kind === 'conditions' && 'bg-secondary',
    // Lit: the edge says so. Quiet: the edge and the shadow soften; the text does not.
    shade === 'lit' ? 'border-foreground' : shade === 'quiet' ? 'border-border-subtle shadow-none' : data.kind === 'lane' || data.kind === 'stage' ? 'border-foreground/40' : 'border-border',
    data.problems.length > 0 && 'border-status-verify-mark',
    // An automation that is off keeps full-contrast text (X1): a dashed edge and ", off" in its label say so.
    (data.disabled || data.kind === 'lane') && 'border-dashed',
    actions && 'group-hover/node:border-foreground group-hover/node:shadow-raised',
  ]);
</script>

<Handle type="target" id="top" position={Position.Top} isConnectable={linksIn} class={linksIn ? '!h-2 !w-12 !opacity-0' : '!size-px !opacity-0'} />
<Handle type="target" id="left" position={Position.Left} isConnectable={false} class="!size-px !opacity-0" />
<div class="group/node relative size-full" data-node-kind={data.kind} data-state={shade}>
  {#if data.kind === 'add-automation'}
    {#if actions}
      <button type="button" class={[frame, 'items-center']} aria-label={data.label} onclick={press}>
        <span
          class={[
            'flex h-8 w-full items-center justify-center gap-1.5 rounded-lg border border-dashed bg-card font-semibold text-muted-foreground shadow-surface motion-safe:transition-[border-color,box-shadow,color]',
            shade === 'lit' ? 'border-foreground' : 'border-foreground/40',
            'group-hover/node:border-foreground group-hover/node:text-foreground group-hover/node:shadow-raised',
          ]}
        >
          <PlusIcon aria-hidden="true" class="size-3.5" />{data.title}
        </span>
      </button>
    {/if}
  {:else if isAdd(data.kind)}
    {#if actions}
      <!-- A plus is icon only, so its name is also its tooltip (A4). Its target is a finger's, and the circle in it is small. -->
      <Popover.Root bind:open={adding}>
        <Tooltip.Root>
          <Tooltip.Trigger>
            {#snippet child({ props })}
              <Popover.Trigger {...props} class="group/plus flex size-full items-center justify-center rounded-full outline-none" aria-label={data.label}>
                <span
                  class={[
                    'flex size-6 items-center justify-center rounded-full border bg-card text-muted-foreground shadow-surface motion-safe:transition-[border-color,box-shadow,color]',
                    shade === 'lit' || adding ? 'border-foreground text-foreground' : 'border-foreground/40',
                    'group-hover/node:border-foreground group-hover/node:text-foreground group-hover/node:shadow-raised',
                    'group-focus-visible/plus:ring-2 group-focus-visible/plus:ring-ring group-focus-visible/plus:ring-offset-2 group-focus-visible/plus:ring-offset-background',
                  ]}
                >
                  <PlusIcon aria-hidden="true" class="size-3.5" />
                </span>
              </Popover.Trigger>
            {/snippet}
          </Tooltip.Trigger>
          <Tooltip.Content>{data.label}</Tooltip.Content>
        </Tooltip.Root>
        <!-- Beside the plus, so the chain it adds to stays in view. -->
        <Popover.Content
          side="right"
          align="center"
          collisionPadding={16}
          class="w-72 max-w-[calc(100vw-2rem)] gap-1 p-1"
          onOpenAutoFocus={(event) => {
            event.preventDefault();
            document.getElementById(`${uid}-add`)?.focus();
          }}
          onCloseAutoFocus={(event) => {
            // A choice opened the automation's sheet, which has the focus now.
            if (chosen) event.preventDefault();
            chosen = false;
          }}
        >
          {#if offer}
            <ChoiceList id={`${uid}-add`} name={data.title} choices={offer.choices} note={offer.note} onchoose={add} />
          {:else}
            <p class="px-2 py-1.5 text-muted-foreground">Nothing can be added here.</p>
          {/if}
        </Popover.Content>
      </Popover.Root>
    {/if}
  {:else}
    {#snippet body()}
      <span class={['flex size-full min-w-0 flex-col gap-1 rounded-lg border px-2.5 py-2', surface]}>
        <span class={['flex items-center gap-1.5 text-muted-foreground', actions && data.removes && 'pr-6']}>
          <!-- A button's name is its own text (WCAG 2.5.3), so what the position adds, which automation and which action,
               is read here, after the word the eye sees. -->
          <span>{data.endStage ? 'End stage' : head ? heading[head] : ''}{data.disabled ? ', off' : ''}{#if spoken}{' '}<span class="sr-only">{spoken}</span>{/if}</span>
        </span>
        <TruncatedText text={data.title} lines={2} tabindex={-1} class="font-semibold" />
        <!-- A trigger's detail is its automation's name, which its label already says after the word When. -->
        <span class="block min-h-4 min-w-0" aria-hidden={data.kind === 'trigger' ? 'true' : undefined}>{#if data.detail}<TruncatedText text={data.detail} tabindex={-1} class="text-muted-foreground" />{/if}</span>
        {#if data.problems.length > 0}
          <span class="flex min-w-0 items-baseline gap-1.5 font-semibold text-status-verify">
            <ProblemIcon class="size-3.5 self-center" /><TruncatedText text={data.problems.join(' ')} lines={2} tabindex={-1} class="flex-1" />
          </span>
        {/if}
      </span>
    {/snippet}
    {#if actions}
      <button type="button" class={frame} onclick={press}>{@render body()}</button>
      {#if data.removes}
        <!-- Inset by its own size on touch, so the 44px target stays inside the node. -->
        <MoreMenu label={menuLabel} size="icon-xs" quiet onclosed={removeOnceClosed} class="nodrag nopan absolute top-1 right-1 transition-none pointer-coarse:top-0 pointer-coarse:right-0 pointer-coarse:items-start pointer-coarse:justify-end pointer-coarse:p-1.5">
          <DropdownMenu.Item onSelect={() => (removing = true)}><Trash2Icon aria-hidden="true" />{data.removes}<DropdownMenu.Shortcut><Kbd>Delete</Kbd></DropdownMenu.Shortcut></DropdownMenu.Item>
        </MoreMenu>
      {/if}
    {:else}
      <!-- A box that cannot be pressed is still one tab stop, so a keyboard user can read the graph node by node (design system section 17). -->
      <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
      <div class={frame} role="group" aria-label={data.label} tabindex="0">{@render body()}</div>
    {/if}
  {/if}
</div>
<Handle type="source" id="bottom" position={Position.Bottom} isConnectable={false} class="!size-px !opacity-0" />
<Handle type="source" id="right" position={Position.Right} isConnectable={linksOut} class={linksOut ? '!size-2.5 !border-foreground !bg-card' : '!size-px !opacity-0'} />
