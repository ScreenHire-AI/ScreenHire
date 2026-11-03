<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import XIcon from '~icons/lucide/x';
  import type { FilterOption } from '#lib/components/filter-menu.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { cn } from '#lib/utils.js';

  // A filter that takes several choices at once (Job: Support Engineer and Senior .NET Engineer): FilterMenu's outline
  // button, opening a checkbox for each choice with its count (T4), and each choice as a pill with its own remove button
  // beside it, so what is filtered shows without opening the menu. It takes `primary` while a choice is set; the page
  // applies it to the rows.
  let {
    label,
    options,
    values = $bindable([]),
  }: {
    /** What it filters, such as "Job". */
    label: string;
    options: readonly FilterOption[];
    /** The chosen options' values; none shows every row. */
    values?: string[];
  } = $props();

  const chosen = $derived(options.filter((option) => values.includes(option.value)));
  const summary = $derived(chosen.length === 0 ? 'All' : chosen.length === 1 ? chosen[0]!.label : `${chosen.length} chosen`);

  const toggle = (value: string, on: boolean) => (values = on ? [...values, value] : values.filter((each) => each !== value));
</script>

<DropdownMenu.Root>
  <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline', size: 'lg' }), 'max-w-64 min-w-0 shrink-0', chosen.length > 0 && 'border-primary text-primary')}>
    <span class="min-w-0 truncate">{label}: {summary}</span>
    <ChevronDownIcon aria-hidden="true" />
  </DropdownMenu.Trigger>
  <DropdownMenu.Content align="start" class="max-h-80 w-72">
    <DropdownMenu.Group>
      <DropdownMenu.GroupHeading class="text-caption text-muted-foreground">{label}</DropdownMenu.GroupHeading>
      {#each options as option (option.value)}
        {@const on = values.includes(option.value)}
        <!-- A visible box on every choice, ticked or not, so the menu reads as "pick several" before anything is picked; it
             replaces the item's own right-hand tick, which would sit on the count. -->
        <DropdownMenu.CheckboxItem
          checked={on}
          onCheckedChange={(next) => toggle(option.value, next)}
          closeOnSelect={false}
          class="gap-2 pr-1.5 [&>[data-slot=dropdown-menu-checkbox-item-indicator]]:hidden"
        >
          <span
            aria-hidden="true"
            class={['grid size-4 shrink-0 place-items-center rounded-sm border', on ? 'border-primary bg-primary text-primary-foreground' : 'border-muted-foreground/70']}
            >{#if on}<CheckIcon class="size-3" />{/if}</span
          >
          <span class="min-w-0 flex-1 break-words">{option.label}</span>
          {#if option.count !== undefined}<Badge variant="count">{option.count}</Badge>{/if}
        </DropdownMenu.CheckboxItem>
      {/each}
    </DropdownMenu.Group>
    {#if chosen.length > 0}
      <DropdownMenu.Separator />
      <DropdownMenu.Item onSelect={() => (values = [])}>Show all</DropdownMenu.Item>
    {/if}
  </DropdownMenu.Content>
</DropdownMenu.Root>
{#if chosen.length > 1}
  {#each chosen as option (option.value)}
    <Badge variant="muted" class="h-8 shrink-0 gap-1 pr-1">
      {option.label}
      <button
        type="button"
        class="relative grid size-6 place-items-center rounded-full hover:bg-muted pointer-coarse:after:absolute pointer-coarse:after:-inset-2.5 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        aria-label={`Remove ${label} ${option.label}`}
        onclick={() => toggle(option.value, false)}><XIcon aria-hidden="true" class="size-3.5" /></button
      >
    </Badge>
  {/each}
{/if}
