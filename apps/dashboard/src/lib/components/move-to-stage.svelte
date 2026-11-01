<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import BookmarkIcon from '~icons/lucide/bookmark';
  import StickyNoteIcon from '~icons/lucide/sticky-note';
  import type { WorkflowStage } from '@screenhire/api-client';
  import { buttonVariants, type ButtonSize, type ButtonVariant } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { cn } from '#lib/utils.js';

  // "Move to stage…": the non-drag way to move an application (WCAG 2.5.7), on every row, card and detail page.
  let {
    candidateName,
    currentStageId,
    stages,
    disabled = false,
    compact = false,
    variant = 'outline',
    size,
    class: className,
    onmove,
    onnote,
    onkeep,
  }: {
    candidateName: string;
    currentStageId: string;
    stages: readonly WorkflowStage[];
    disabled?: boolean;
    /** A short "Move" trigger for table rows and cards. */
    compact?: boolean;
    /** A detail page's primary action is filled. */
    variant?: ButtonVariant;
    /** The trigger's size when not compact; `sm` by default. */
    size?: ButtonSize;
    class?: string;
    onmove: (stage: WorkflowStage) => void;
    /** Adds "With a note", which opens a sheet to move with a note (the detail page's). */
    onnote?: () => void;
    /**
     * Adds "Keep in mind" after the stages, beside Rejected (docs/architecture/talent-pool.md): the candidate leaves the
     * job without a rejection. Only for a candidate who allowed it, so the page leaves it out otherwise (A3).
     */
    onkeep?: () => void;
  } = $props();
</script>

<DropdownMenu.Root>
  <!-- Named by its visible words plus who it moves, so speech input can say what it sees (WCAG 2.5.3). An aria-label
       rather than hidden text: Chromium puts a space around an absolutely positioned span, which named the full
       trigger "Move to stage , Robin Ashby". -->
  <DropdownMenu.Trigger
    {disabled}
    aria-label={compact ? `Move ${candidateName} to another stage` : `Move to stage, ${candidateName}`}
    class={cn(buttonVariants({ variant, size: compact ? 'xs' : (size ?? 'sm') }), className)}
  >
    <ArrowRightIcon aria-hidden="true" />{compact ? 'Move' : 'Move to stage'}
  </DropdownMenu.Trigger>
  <DropdownMenu.Content align="end" class="w-52">
    <DropdownMenu.Label class="text-caption text-muted-foreground">Move to</DropdownMenu.Label>
    {#each stages as stage (stage.id)}
      <DropdownMenu.Item disabled={stage.id === currentStageId} onSelect={() => onmove(stage)}>
        {stage.name}
        {#if stage.id === currentStageId}<span class="ml-auto text-caption text-muted-foreground">current</span>{/if}
      </DropdownMenu.Item>
    {/each}
    {#if onkeep}
      <DropdownMenu.Separator />
      <DropdownMenu.Item onSelect={onkeep}><BookmarkIcon aria-hidden="true" />Keep in mind…</DropdownMenu.Item>
    {/if}
    {#if onnote}
      <DropdownMenu.Separator />
      <DropdownMenu.Item onSelect={onnote}><StickyNoteIcon aria-hidden="true" />With a note…</DropdownMenu.Item>
    {/if}
  </DropdownMenu.Content>
</DropdownMenu.Root>
