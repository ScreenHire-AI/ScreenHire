<script lang="ts">
  import { Handle, Position, type NodeProps } from '@xyflow/svelte';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import { cn } from '#lib/utils.js';
  import type { StepNode } from '#lib/workflow-graph.js';
  import ProblemMark from './problem-mark.svelte';

  // One node of the workflow graph. Automation nodes carry an Edit button, the graph's keyboard and pointer path to the
  // automation's form; the nodes themselves are not focusable or draggable.
  let { data }: NodeProps<StepNode> = $props();

  const kindLabel = { stage: 'Stage', trigger: 'When', conditions: 'If', action: 'Then' } as const;
  const edit = $derived(data.onedit as ((automation: number) => void) | undefined);
</script>

<Handle type="target" position={Position.Left} isConnectable={false} />
<div
  class={[
    'flex w-[200px] flex-col gap-0.5 rounded-lg border bg-card px-2.5 py-2 text-left text-caption text-card-foreground',
    data.kind === 'stage' ? 'border-foreground/40' : 'border-border',
    data.invalid ? 'border-destructive' : '',
    data.disabled ? 'opacity-60' : '',
  ]}
>
  <span class="text-caption text-muted-foreground">{kindLabel[data.kind]}{data.disabled ? ', off' : ''}</span>
  <span class="leading-snug font-semibold">{data.title}</span>
  {#if data.detail}<span class="line-clamp-2 text-muted-foreground">{data.detail}</span>{/if}
  {#if data.invalid}
    <span class="inline-flex items-center gap-1.5 font-semibold text-destructive"><ProblemMark />Needs a fix</span>
  {/if}
  {#if data.kind === 'trigger' && data.automation !== null && edit}
    <button
      type="button"
      class={cn(buttonVariants({ variant: 'outline', size: 'xs' }), 'nodrag nopan mt-1 self-start')}
      onclick={() => edit(data.automation!)}>Edit <span class="sr-only">{data.title}</span></button
    >
  {/if}
</div>
<Handle type="source" position={Position.Right} isConnectable={false} />
