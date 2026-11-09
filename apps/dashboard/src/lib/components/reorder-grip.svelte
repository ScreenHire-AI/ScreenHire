<script lang="ts">
  import ArrowDownIcon from '~icons/lucide/arrow-down';
  import ArrowDownToLineIcon from '~icons/lucide/arrow-down-to-line';
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import ArrowLeftToLineIcon from '~icons/lucide/arrow-left-to-line';
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import ArrowRightToLineIcon from '~icons/lucide/arrow-right-to-line';
  import ArrowUpIcon from '~icons/lucide/arrow-up';
  import ArrowUpToLineIcon from '~icons/lucide/arrow-up-to-line';
  import GripVerticalIcon from '~icons/lucide/grip-vertical';
  import { mergeProps } from 'bits-ui';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // An item's reorder grip (design system A7), rendered by `ReorderList`, which owns the drag. Drag it to move the item;
  // click it (or press Enter) for Move up, Move down, Move to top and Move to bottom; press Space to pick the item up for
  // the arrow keys (Move left, Move right, Move to start and Move to end in a horizontal list, as the stage cards are). Moves that do not apply are left out (A3). It is a `role="button"` span rather than a `<button>`:
  // svelte-dnd-action leaves keys on a native button alone, so Space would never start the keyboard drag.
  let {
    label,
    index,
    count,
    orientation = 'vertical',
    onactivate,
    onmove,
  }: {
    /** The item's name: the grip is "Reorder {label}". */
    label: string;
    index: number;
    count: number;
    /** A horizontal list's moves are left and right, start and end. */
    orientation?: 'vertical' | 'horizontal';
    /** Lets the list's drag start from this grip, for the pointer press or key press under way. */
    onactivate: (from: 'pointer' | 'keyboard') => void;
    onmove: (to: number) => void;
  } = $props();

  let grip = $state<HTMLElement | null>(null);
  let open = $state(false);
  // The tooltip stays shut while the menu is open, so it never covers the rows above the menu (A4).
  let tooltipOpen = $state(false);
  const name = $derived(`Reorder ${label}`);

  // The press listeners sit on the grip itself, not on Svelte's delegated handlers at the document root: the list's drag
  // zone has to be switched on before the same event bubbles up to its item.
  function presses(element: HTMLElement) {
    const touch = () => onactivate('pointer');
    element.addEventListener('mousedown', onmousedown);
    element.addEventListener('touchstart', touch, { passive: true });
    element.addEventListener('keydown', onkeydown);
    return () => {
      element.removeEventListener('mousedown', onmousedown);
      element.removeEventListener('touchstart', touch);
      element.removeEventListener('keydown', onkeydown);
    };
  }

  function onmousedown(event: MouseEvent) {
    if (event.button !== 0) return;
    // No focus ring from a press: the drag or the menu takes over.
    event.preventDefault();
    onactivate('pointer');
  }

  function onkeydown(event: KeyboardEvent) {
    if (event.key === ' ') {
      // The list's drag zone, further up, picks the item up on this same key press.
      onactivate('keyboard');
    } else if (event.key === 'Enter') {
      event.preventDefault();
      open = true;
    }
  }
</script>

<DropdownMenu.Root bind:open>
  <Tooltip.Root bind:open={() => tooltipOpen && !open, (next) => (tooltipOpen = next)}>
    <Tooltip.Trigger>
      {#snippet child({ props })}
        <span
          bind:this={grip}
          {...mergeProps(props, { onclick: () => (open = true) })}
          {@attach presses}
          role="button"
          tabindex="0"
          aria-label={name}
          aria-haspopup="menu"
          aria-expanded={open}
          data-reorder-grip
          class="inline-flex size-8 shrink-0 cursor-grab touch-none items-center justify-center rounded-lg text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring pointer-coarse:size-11"
        >
          <GripVerticalIcon aria-hidden="true" />
        </span>
      {/snippet}
    </Tooltip.Trigger>
    <Tooltip.Content>{name}</Tooltip.Content>
  </Tooltip.Root>
  <DropdownMenu.Content
    customAnchor={grip}
    class="w-auto min-w-48"
    onCloseAutoFocus={(event) => {
      // There is no menu trigger to return to: the grip is the drag handle, so it gets the focus back itself.
      event.preventDefault();
      grip?.focus();
    }}
  >
    {@const across = orientation === 'horizontal'}
    {#if index > 0}
      <DropdownMenu.Item onSelect={() => onmove(index - 1)}>
        {#if across}<ArrowLeftIcon aria-hidden="true" />Move left{:else}<ArrowUpIcon aria-hidden="true" />Move up{/if}
      </DropdownMenu.Item>
    {/if}
    {#if index < count - 1}
      <DropdownMenu.Item onSelect={() => onmove(index + 1)}>
        {#if across}<ArrowRightIcon aria-hidden="true" />Move right{:else}<ArrowDownIcon aria-hidden="true" />Move down{/if}
      </DropdownMenu.Item>
    {/if}
    {#if index > 0}
      <DropdownMenu.Item onSelect={() => onmove(0)}>
        {#if across}<ArrowLeftToLineIcon aria-hidden="true" />Move to start{:else}<ArrowUpToLineIcon aria-hidden="true" />Move to top{/if}
      </DropdownMenu.Item>
    {/if}
    {#if index < count - 1}
      <DropdownMenu.Item onSelect={() => onmove(count - 1)}>
        {#if across}<ArrowRightToLineIcon aria-hidden="true" />Move to end{:else}<ArrowDownToLineIcon aria-hidden="true" />Move to bottom{/if}
      </DropdownMenu.Item>
    {/if}
  </DropdownMenu.Content>
</DropdownMenu.Root>
