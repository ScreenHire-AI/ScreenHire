<script lang="ts">
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowDownToLineIcon from '@lucide/svelte/icons/arrow-down-to-line';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import ArrowUpToLineIcon from '@lucide/svelte/icons/arrow-up-to-line';
  import GripVerticalIcon from '@lucide/svelte/icons/grip-vertical';
  import { mergeProps } from 'bits-ui';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // An item's reorder grip (design system A7), rendered by `ReorderList`, which owns the drag. Drag it to move the item;
  // click it (or press Enter) for Move up, Move down, Move to top and Move to bottom; press Space to pick the item up for
  // the arrow keys. Moves that do not apply are left out (A3). It is a `role="button"` span rather than a `<button>`:
  // svelte-dnd-action leaves keys on a native button alone, so Space would never start the keyboard drag.
  let {
    label,
    index,
    count,
    onactivate,
    onmove,
  }: {
    /** The item's name: the grip is "Reorder {label}". */
    label: string;
    index: number;
    count: number;
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
    {#if index > 0}
      <DropdownMenu.Item onSelect={() => onmove(index - 1)}><ArrowUpIcon aria-hidden="true" />Move up</DropdownMenu.Item>
    {/if}
    {#if index < count - 1}
      <DropdownMenu.Item onSelect={() => onmove(index + 1)}><ArrowDownIcon aria-hidden="true" />Move down</DropdownMenu.Item>
    {/if}
    {#if index > 0}
      <DropdownMenu.Item onSelect={() => onmove(0)}><ArrowUpToLineIcon aria-hidden="true" />Move to top</DropdownMenu.Item>
    {/if}
    {#if index < count - 1}
      <DropdownMenu.Item onSelect={() => onmove(count - 1)}><ArrowDownToLineIcon aria-hidden="true" />Move to bottom</DropdownMenu.Item>
    {/if}
  </DropdownMenu.Content>
</DropdownMenu.Root>
