<script lang="ts" module>
  /** What a node's buttons do; the graph passes them only when the workflow can be edited on this screen. */
  export interface GraphActions {
    /** Opens the automation's form at the part `path` names (its trigger, conditions or an action). */
    edit: (automation: number, path: string) => void;
    /** Adds an automation to a stage's lane, or the whole workflow's (null), and opens its form. */
    add: (stageId: string | null) => void;
    remove: (automationId: string) => void;
    /** Points the "move to a stage" action node `source` at the stage node `target` (a link drawn with the pointer). */
    connect: (source: string, target: string) => void;
  }
</script>

<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import Trash2Icon from '~icons/lucide/trash-2';
  import { Handle, Position, type NodeProps } from '@xyflow/svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import ProblemIcon from '#lib/components/problem-icon.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { NODE_WIDTH, type StepNode } from '#lib/workflow-graph.js';

  // One node of the workflow graph, itself a tab stop (the graph moves between nodes with the arrow keys). Its buttons
  // are the graph's keyboard and pointer path to editing: a lane or stage adds an automation, an automation's nodes open
  // its form at their part, and a trigger node removes its automation. The node's text is clamped so its height has a
  // bound (`nodeHeight`), with the full value in a tooltip; the tooltips take no tab stop, because the node is one.
  let { data, isConnectable }: NodeProps<StepNode> = $props();

  const kindLabel = { lane: '', stage: 'Stage', trigger: 'When', conditions: 'If', action: 'Then' } as const;
  const actions = $derived(data.actions as GraphActions | undefined);
  // Only a "move to a stage" action draws a link, and only to a stage: the pointer shortcut for the action's Stage field.
  const linksOut = $derived(Boolean(data.movesToStage) && isConnectable);
  const linksIn = $derived(data.kind === 'stage' && isConnectable);
  const part = $derived(
    data.kind === 'conditions' ? `the conditions of ${data.automationName}` : data.kind === 'action' ? `${data.title} in ${data.automationName}` : data.title,
  );
</script>

<Handle type="target" position={Position.Left} isConnectable={linksIn} />
<div
  style:width="{NODE_WIDTH}px"
  class={[
    'flex flex-col gap-1 rounded-lg border bg-card px-2.5 py-2 text-left text-caption text-card-foreground shadow-surface',
    data.kind === 'stage' || data.kind === 'lane' ? 'border-foreground/40' : 'border-border',
    data.problems.length > 0 ? 'border-status-verify-mark' : '',
    // An automation that is off keeps full-contrast text (X1): a dashed edge and ", off" in its label say so.
    data.disabled ? 'border-dashed' : '',
  ]}
>
  {#if kindLabel[data.kind]}<span class="text-muted-foreground">{kindLabel[data.kind]}{data.disabled ? ', off' : ''}</span>{/if}
  <TruncatedText text={data.title} lines={2} tabindex={-1} class="font-semibold" />
  {#if data.detail}<TruncatedText text={data.detail} tabindex={-1} class="text-muted-foreground" />{/if}
  {#if data.problems.length > 0}
    <span class="flex min-w-0 items-baseline gap-1.5 font-semibold text-status-verify">
      <ProblemIcon class="size-3.5 self-center" /><TruncatedText text={data.problems.join(' ')} lines={2} tabindex={-1} class="flex-1" />
    </span>
  {/if}
  {#if actions}
    <div class="nodrag nopan mt-1 flex items-center gap-1">
      {#if data.kind === 'lane' || data.kind === 'stage'}
        <Button variant="outline" size="xs" onclick={() => actions.add(data.lane)}>
          <PlusIcon aria-hidden="true" />Add automation <span class="sr-only">to {data.title}</span>
        </Button>
      {:else if data.automation !== null}
        <Button variant="outline" size="xs" onclick={() => actions.edit(data.automation!, data.path)}>Edit <span class="sr-only">{part}</span></Button>
        {#if data.kind === 'trigger' && data.automationId}
          <IconButton size="icon-xs" label={`Remove ${data.automationName || 'this automation'}`} onclick={() => actions.remove(data.automationId!)}>
            <Trash2Icon />
          </IconButton>
        {/if}
      {/if}
    </div>
  {/if}
</div>
<Handle type="source" position={Position.Right} isConnectable={linksOut} />
