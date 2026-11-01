<script lang="ts" generics="Stage extends { id: string; name: string }">
  import BookmarkIcon from '~icons/lucide/bookmark';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import EmailSelectionButton from '#lib/components/email-selection-button.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { cn } from '#lib/utils.js';

  // The bar a list panel shows while rows are ticked (design system section 21): how many, then Move, Keep in mind and
  // Email for them, and Clear selection at the end. The page decides what is offered: the stages come from
  // `bulkMoveStages`, and Keep in mind and Email are left out while a verification filter picked the rows (ADR-0004). It says the count once (W4), so the buttons' names
  // carry it only for screen readers.
  let {
    count,
    stages,
    onmove,
    onemail,
    onkeep,
    onclear,
    disabled = false,
  }: {
    count: number;
    /** The stages a move of the selection may go to. */
    stages: readonly Stage[];
    /** Moves the selection; left out when the person's role cannot move candidates. */
    onmove?: (stage: Stage) => unknown;
    /** Opens the email composer for the selection; left out when the selection may not be emailed. */
    onemail?: () => void;
    /**
     * Opens Keep in mind for the selection (docs/architecture/talent-pool.md); left out when the person cannot move
     * candidates or a Verification filter picked the rows, since it ends their candidacy on this job (ADR-0004).
     */
    onkeep?: () => void;
    onclear: () => void;
    /** While a move is in flight. */
    disabled?: boolean;
  } = $props();
</script>

<div
  role="region"
  aria-label="Selected candidates"
  class="flex min-w-0 items-center gap-2 overflow-x-auto border-b border-border-subtle bg-row-selected px-4.5 py-2 max-md:px-4"
>
  <span class="shrink-0 font-semibold whitespace-nowrap text-accent-foreground" data-selected-count>{count} selected</span>
  {#if onmove}
    <DropdownMenu.Root>
      <DropdownMenu.Trigger aria-label="Move {count} selected…" {disabled} class={cn(buttonVariants({ variant: 'outline', size: 'lg' }), 'shrink-0')}>
        Move<ChevronDownIcon aria-hidden="true" />
      </DropdownMenu.Trigger>
      <DropdownMenu.Content align="start" class="w-52">
        {#each stages as stage (stage.id)}
          <DropdownMenu.Item onSelect={() => onmove(stage)}>{stage.name}</DropdownMenu.Item>
        {/each}
      </DropdownMenu.Content>
    </DropdownMenu.Root>
  {/if}
  {#if onkeep}
    <Button variant="outline" size="lg" class="shrink-0" aria-label="Keep in mind, {count} selected" {disabled} onclick={onkeep}>
      <BookmarkIcon aria-hidden="true" />Keep in mind
    </Button>
  {/if}
  {#if onemail}
    <EmailSelectionButton {count} onclick={onemail} size="lg" />
  {/if}
  <Button variant="outline" size="lg" class="ml-auto shrink-0" onclick={onclear}>Clear selection</Button>
</div>
