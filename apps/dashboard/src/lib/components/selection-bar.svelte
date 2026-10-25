<script lang="ts" generics="Stage extends { id: string; name: string }">
  import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
  import EmailSelectionButton from '#lib/components/email-selection-button.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { cn } from '#lib/utils.js';

  // The bar a list panel shows while rows are ticked (design system section 22): how many, then Move and Email for them,
  // and Clear selection at the end. The page decides what is offered: the stages come from `bulkMoveStages`, and Email
  // is left out while an integrity filter picked the rows (ADR-0004). It says the count once (W4), so the buttons' names
  // carry it only for screen readers.
  let {
    count,
    stages,
    onmove,
    onemail,
    onclear,
    disabled = false,
  }: {
    count: number;
    /** The stages a move of the selection may go to. */
    stages: readonly Stage[];
    onmove: (stage: Stage) => unknown;
    /** Opens the email composer for the selection; left out when the selection may not be emailed. */
    onemail?: () => void;
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
  {#if onemail}
    <EmailSelectionButton {count} onclick={onemail} size="lg" />
  {/if}
  <Button variant="outline" size="lg" class="ml-auto shrink-0" onclick={onclear}>Clear selection</Button>
</div>
