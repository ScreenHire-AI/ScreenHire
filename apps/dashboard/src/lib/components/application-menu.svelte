<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';

  // A candidate row's menu (design system L3): what can be done to one application from a list, so no button repeats
  // down the column. Today that is moving it, the non-drag way (WCAG 2.5.7), with the stage it is in marked. A job's
  // table rows and the board's cards share it; the application's own page keeps `MoveToStage`, its one action.
  let {
    candidateName,
    currentStageId,
    stages,
    onmove,
  }: {
    candidateName: string;
    /** The stage the application is in; empty for a saved candidate, who is in none. */
    currentStageId: string;
    stages: readonly WorkflowStage[];
    onmove: (stage: WorkflowStage) => void;
  } = $props();
</script>

<MoreMenu label={`Actions for ${candidateName}`} size="icon" quiet>
  <DropdownMenu.Label class="text-caption text-muted-foreground">Move to</DropdownMenu.Label>
  {#each stages as stage (stage.id)}
    <DropdownMenu.Item disabled={stage.id === currentStageId} onSelect={() => onmove(stage)}>
      {stage.name}
      {#if stage.id === currentStageId}<span class="ml-auto text-caption text-muted-foreground">current</span>{/if}
    </DropdownMenu.Item>
  {/each}
</MoreMenu>
