<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { cn } from '#lib/utils.js';

  // "Move to stage…": the non-drag way to move an application (WCAG 2.5.7), on every row, card and detail page.
  let {
    candidateName,
    currentStageId,
    stages,
    disabled = false,
    compact = false,
    onmove,
  }: {
    candidateName: string;
    currentStageId: string;
    stages: readonly WorkflowStage[];
    disabled?: boolean;
    /** A short "Move" trigger for table rows and cards. */
    compact?: boolean;
    onmove: (stage: WorkflowStage) => void;
  } = $props();
</script>

<DropdownMenu.Root>
  <!-- Named by its visible words plus who it moves, so speech input can say what it sees (WCAG 2.5.3). -->
  <DropdownMenu.Trigger {disabled} class={cn(buttonVariants({ variant: 'outline', size: compact ? 'xs' : 'sm' }))}>
    {#if compact}
      Move <span class="sr-only">{candidateName} to another stage</span>
    {:else}
      Move to stage<span class="sr-only">, {candidateName}</span>
    {/if}
  </DropdownMenu.Trigger>
  <DropdownMenu.Content align="end" class="w-52">
    <DropdownMenu.Label class="font-mono text-[10.5px] tracking-[0.06em] text-muted-foreground uppercase">Move to</DropdownMenu.Label>
    {#each stages as stage (stage.id)}
      <DropdownMenu.Item disabled={stage.id === currentStageId} onSelect={() => onmove(stage)}>
        {stage.name}
        {#if stage.id === currentStageId}<span class="ml-auto text-xs text-muted-foreground">current</span>{/if}
      </DropdownMenu.Item>
    {/each}
  </DropdownMenu.Content>
</DropdownMenu.Root>
