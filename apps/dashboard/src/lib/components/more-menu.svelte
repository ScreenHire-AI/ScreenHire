<script lang="ts">
  import EllipsisIcon from '~icons/lucide/ellipsis';
  import type { Snippet } from 'svelte';
  import { buttonVariants, type ButtonSize } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { cn } from '#lib/utils.js';

  // The More menu: an icon button with its label in a tooltip (A4) that opens the actions that do not earn a button,
  // destructive ones included (A2, section 10). Fill it with `DropdownMenu.Item`s.
  let {
    label,
    size = 'icon-lg',
    quiet = false,
    onclosed,
    class: className,
    children,
  }: {
    /** The button's name, such as "More actions" or "Actions for Senior .NET Engineer". */
    label: string;
    size?: Extract<ButtonSize, 'icon' | 'icon-sm' | 'icon-lg'>;
    /** A row action: an icon with no edge (section 10). */
    quiet?: boolean;
    /**
     * Runs once the menu has closed and focus is back on its button. An item that opens a dialog opens it here: a
     * dialog opened from the item itself loses focus to the closing menu, and then cannot give focus back.
     */
    onclosed?: () => void;
    class?: string;
    children: Snippet;
  } = $props();
</script>

<DropdownMenu.Root>
  <Tooltip.Root>
    <Tooltip.Trigger>
      {#snippet child({ props })}
        <DropdownMenu.Trigger {...props} aria-label={label} class={cn(buttonVariants({ variant: quiet ? 'ghost' : 'outline', size }), className)}>
          <EllipsisIcon aria-hidden="true" />
        </DropdownMenu.Trigger>
      {/snippet}
    </Tooltip.Trigger>
    <Tooltip.Content>{label}</Tooltip.Content>
  </Tooltip.Root>
  <DropdownMenu.Content align="end" class="min-w-52" onCloseAutoFocus={() => onclosed && setTimeout(onclosed)}>
    {@render children()}
  </DropdownMenu.Content>
</DropdownMenu.Root>
