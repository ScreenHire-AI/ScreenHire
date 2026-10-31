<script lang="ts" module>
  /** One choice of a filter menu. */
  export interface FilterOption {
    value: string;
    label: string;
    count?: number;
  }
</script>

<script lang="ts">
  import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { cn } from '#lib/utils.js';

  // A filter with many choices (a job's stages), or a few on a phone where chips do not fit: an outline button that names
  // the filter and its choice ("Stage: All"), opening every choice with its count in the count badge (T4). Like the
  // verification and tag filters, it takes `primary` while a choice is set, and the page applies it to the rows.
  let {
    label,
    options,
    value = $bindable(null),
    total,
    onchange,
    class: className,
  }: {
    /** What it filters, such as "Stage". */
    label: string;
    options: readonly FilterOption[];
    /** The chosen option's value; null shows every row. */
    value?: string | null;
    /** The count beside All. */
    total?: number;
    /** Called with each new choice, after `value` changes. */
    onchange?: (value: string | null) => void;
    class?: string;
  } = $props();

  const chosen = $derived(options.find((option) => option.value === value));
  // bits-ui radio groups take strings; a key no option can have stands for All.
  const all = '\u0000all';

  function choose(next: string) {
    value = next === all ? null : next;
    onchange?.(value);
  }
</script>

<DropdownMenu.Root>
  <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline', size: 'lg' }), 'max-w-64 min-w-0 shrink-0', chosen && 'border-primary text-primary', className)}>
    <span class="min-w-0 truncate">{label}: {chosen?.label ?? 'All'}</span>
    <ChevronDownIcon aria-hidden="true" />
  </DropdownMenu.Trigger>
  <DropdownMenu.Content align="start" class="max-h-80 min-w-56">
    <DropdownMenu.Group>
      <DropdownMenu.GroupHeading class="text-caption text-muted-foreground">{label}</DropdownMenu.GroupHeading>
      <DropdownMenu.RadioGroup value={value ?? all} onValueChange={choose}>
        {@render item(all, 'All', total)}
        {#each options as option (option.value)}
          {@render item(option.value, option.label, option.count)}
        {/each}
      </DropdownMenu.RadioGroup>
    </DropdownMenu.Group>
  </DropdownMenu.Content>
</DropdownMenu.Root>

{#snippet item(itemValue: string, itemLabel: string, count: number | undefined)}
  <DropdownMenu.RadioItem value={itemValue} closeOnSelect>
    <span class="min-w-0 flex-1 break-words">{itemLabel}</span>
    {#if count !== undefined}<Badge variant="count">{count}</Badge>{/if}
  </DropdownMenu.RadioItem>
{/snippet}
