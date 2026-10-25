<script lang="ts" module>
  /** One thing a picker offers: its id, the name it is found by, and one quiet line beside it. */
  export interface PickerOption {
    id: string;
    label: string;
    hint?: string;
  }
</script>

<script lang="ts">
  import type { Component } from 'svelte';
  import * as Command from '#lib/components/ui/command/index.js';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import { cn } from '#lib/utils.js';

  // Picker (design system section 21): an outline button that opens a searchable list and adds the one chosen, such as
  // a person to a hiring team or the job to copy one from. Typing filters by name; Enter or a click picks, and the list
  // closes. Nothing to pick is said in the list, never by disabling the button (A3).
  let {
    label,
    icon: Icon,
    options,
    searchLabel,
    empty,
    onpick,
  }: {
    /** The button's verb ("Add a person"). */
    label: string;
    icon?: Component;
    options: readonly PickerOption[];
    /** The search field's label ("Search people"). */
    searchLabel: string;
    /** What the list says when nothing matches or nothing is left. */
    empty: string;
    onpick: (id: string) => void;
  } = $props();

  let open = $state(false);
  let query = $state('');
  // Filtered here, by the words of the name, rather than by the list's own fuzzy match, which would also match ids.
  const words = $derived(query.toLocaleLowerCase().split(/\s+/).filter(Boolean));
  const shown = $derived(words.length === 0 ? options : options.filter((option) => words.every((word) => option.label.toLocaleLowerCase().includes(word))));
</script>

<Popover.Root bind:open onOpenChange={(next) => next || (query = '')}>
  <Popover.Trigger class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>
    {#if Icon}<Icon aria-hidden="true" />{/if}{label}
  </Popover.Trigger>
  <Popover.Content align="start" collisionPadding={16} class="w-80 max-w-[calc(100vw-2rem)] p-0">
    <Command.Root shouldFilter={false}>
      <Command.Input bind:value={query} placeholder={searchLabel} aria-label={searchLabel} />
      <Command.List>
        <Command.Empty>{empty}</Command.Empty>
        {#each shown as option (option.id)}
          <Command.Item
            value={option.id}
            onSelect={() => {
              open = false;
              onpick(option.id);
            }}
          >
            <span class="flex min-w-0 flex-col">
              <span class="truncate font-semibold">{option.label}</span>
              {#if option.hint}<span class="truncate text-caption text-muted-foreground">{option.hint}</span>{/if}
            </span>
          </Command.Item>
        {/each}
      </Command.List>
    </Command.Root>
  </Popover.Content>
</Popover.Root>
