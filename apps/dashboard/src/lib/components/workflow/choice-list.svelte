<script lang="ts">
  import { Command as CommandPrimitive } from 'bits-ui';
  import * as Command from '#lib/components/ui/command/index.js';
  import type { Choice } from '#lib/automation-sentence.js';

  // A short list of plain choices in a popover, grouped where the choices are many: a blank of the sentence, the kinds of
  // step or condition a plus on the map adds, and the values a condition in Rules ticks. `selected` ticks what is chosen;
  // what a press does is the caller's. It ends with one quiet line where choices were left out (the guardrail's).
  let {
    id,
    name,
    choices,
    selected = [],
    note,
    onchoose,
  }: {
    /** The field's id. The popover focuses it as it opens, because the first thing bits-ui can tab to is sometimes the popover. */
    id: string;
    /** What the list is for, as its search names it. */
    name: string;
    choices: readonly Choice[];
    selected?: readonly string[];
    note?: string;
    onchoose: (id: string) => void;
  } = $props();

  let query = $state('');
  // A search only past about ten choices (S9): a short list reads faster than it filters.
  const searchable = $derived(choices.length > 10);
  const words = $derived(query.toLocaleLowerCase().split(/\s+/).filter(Boolean));
  const shown = $derived(words.length === 0 ? choices : choices.filter((choice) => words.every((word) => choice.label.toLocaleLowerCase().includes(word))));
  const headings = $derived([...new Set(shown.map((choice) => choice.group ?? ''))]);
</script>

<Command.Root shouldFilter={false} class="bg-transparent">
  <!-- The field is what takes the keyboard (the arrow keys, Enter, and the option a screen reader reads), so a short list has
       it too, out of sight, with no search in it and without bringing up a touch keyboard. aria-controls: the combobox role
       requires it. The viewport is where bits-ui looks for the option that aria-activedescendant names. -->
  <div class={searchable ? undefined : 'sr-only'}>
    <Command.Input
      {id}
      bind:value={() => query, (next) => (query = searchable ? next : '')}
      inputmode={searchable ? undefined : 'none'}
      placeholder={searchable ? 'Search' : undefined}
      aria-controls={`${id}-list`}
      aria-label={searchable ? `Search ${name.toLowerCase()}` : name}
    />
  </div>
  <!-- A list longer than its box scrolls by the arrow keys from the field, and takes the focus itself for a pointer (axe:
       scrollable-region-focusable). -->
  <Command.List id={`${id}-list`} class="max-h-[min(24rem,60svh)]" tabindex={0} aria-label={name}>
    <CommandPrimitive.Viewport>
      <Command.Empty>Nothing matches.</Command.Empty>
      {#each headings as heading (heading)}
        {#snippet items()}
          {#each shown.filter((choice) => (choice.group ?? '') === heading) as choice (choice.id)}
            {@const chosen = selected.includes(choice.id)}
            <Command.Item value={choice.id} data-checked={chosen ? 'true' : undefined} aria-selected={chosen} onSelect={() => onchoose(choice.id)}>
              <span class="min-w-0 break-words">{choice.label}</span>
            </Command.Item>
          {/each}
        {/snippet}
        {#if heading}
          <Command.Group heading={heading}>{@render items()}</Command.Group>
        {:else}
          <Command.Group>{@render items()}</Command.Group>
        {/if}
      {/each}
    </CommandPrimitive.Viewport>
  </Command.List>
</Command.Root>
{#if note}<p class="px-2 py-1.5 text-caption text-muted-foreground">{note}</p>{/if}
