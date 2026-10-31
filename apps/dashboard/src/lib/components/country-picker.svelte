<script lang="ts">
  import CheckIcon from '@lucide/svelte/icons/check';
  import XIcon from '@lucide/svelte/icons/x';
  import { Combobox } from 'bits-ui';
  import { tick } from 'svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { countryName, countryOptions, matchCountries } from '#lib/countries.js';
  import { locales } from '#lib/dates.js';

  // Country picker (design system section 11, W8): a search field that finds countries by name in the person's
  // languages, and each chosen country a fact pill with its own Remove button beside it (A5). The value is the ISO
  // codes the API takes. The field is bits-ui's combobox, which owns the roles and keys: typing or the down arrow
  // opens the list, the arrow keys move, Enter or a click adds a country (or takes a ticked one away) and the list
  // stays open for the next, Esc closes it. `id` is the field's, for the form's label.
  let {
    value = $bindable([]),
    id,
    invalid = false,
    describedby,
  }: {
    value?: string[];
    id: string;
    invalid?: boolean;
    describedby?: string;
  } = $props();

  let open = $state(false);
  let query = $state('');
  let announcement = $state('');
  let list = $state<HTMLUListElement | null>(null);
  let field = $state<HTMLInputElement | null>(null);

  const listId = $derived(`${id}-list`);
  const options = $derived(countryOptions(locales, value));
  const shown = $derived(matchCountries(options, query));
  const name = (code: string) => countryName(locales, code);
  const chosen = (count: number) => `${count} ${count === 1 ? 'country' : 'countries'} chosen.`;

  function picked(next: string[], previous: readonly string[]) {
    const added = next.find((code) => !previous.includes(code));
    const removed = previous.find((code) => !next.includes(code));
    announcement = added ? `${name(added)} added. ${chosen(next.length)}` : removed ? `${name(removed)} removed. ${chosen(next.length)}` : '';
    // The search starts again for the next country; the combobox would otherwise show the last name picked.
    query = '';
  }

  async function remove(code: string, index: number) {
    value = value.filter((other) => other !== code);
    announcement = `${name(code)} removed. ${chosen(value.length)}`;
    // Focus goes to the next pill's Remove button, the last one when this was last, or the field when none is left.
    await tick();
    const buttons = list?.querySelectorAll('button') ?? [];
    (buttons[Math.min(index, buttons.length - 1)] ?? field)?.focus();
  }
</script>

<div class="flex min-w-0 flex-col gap-2">
  {#if value.length > 0}
    <ul bind:this={list} aria-label="Chosen countries" class="flex flex-wrap gap-x-1 gap-y-2">
      {#each value as code, index (code)}
        <li class="flex max-w-full min-w-0 items-center">
          <Badge variant="muted" class="min-w-0"><TruncatedText text={name(code)} /></Badge>
          <IconButton label={`Remove ${name(code)}`} onclick={() => remove(code, index)}><XIcon aria-hidden="true" /></IconButton>
        </li>
      {/each}
    </ul>
  {/if}
  <Combobox.Root
    type="multiple"
    bind:value={() => value, (next) => {
      picked(next, value);
      value = next;
    }}
    bind:open
    onOpenChange={(next) => next || (query = '')}
  >
    <Combobox.Input
      {id}
      bind:ref={field}
      placeholder="Search countries"
      autocomplete="off"
      aria-invalid={invalid ? 'true' : undefined}
      aria-describedby={describedby}
      aria-controls={open ? listId : undefined}
      oninput={(event) => (query = event.currentTarget.value)}
      onclick={() => (open = true)}
    >
      {#snippet child({ props })}
        <!-- The field shows the search, never the last name picked. -->
        <Input {...props} value={query} />
      {/snippet}
    </Combobox.Input>
    <Combobox.Portal>
      <Combobox.Content
        id={listId}
        aria-label="Countries"
        sideOffset={4}
        collisionPadding={16}
        class="z-50 max-h-72 w-(--bits-combobox-anchor-width) max-w-[calc(100vw-2rem)] overflow-y-auto rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-overlay"
      >
        {#each shown as option (option.value)}
          <Combobox.Item
            value={option.value}
            label={option.label}
            class="flex min-h-9 cursor-default items-center gap-2 rounded-md px-2.5 py-1 text-body outline-hidden select-none data-highlighted:bg-accent pointer-coarse:min-h-11"
          >
            {#snippet children({ selected })}
              <span class="min-w-0 flex-1 truncate">{option.label}</span>
              <CheckIcon aria-hidden="true" class={selected ? 'size-4 shrink-0' : 'size-4 shrink-0 opacity-0'} />
            {/snippet}
          </Combobox.Item>
        {:else}
          <p class="px-2.5 py-2 text-muted-foreground">No country matches.</p>
        {/each}
      </Combobox.Content>
    </Combobox.Portal>
  </Combobox.Root>
  <p role="status" class="sr-only">{announcement}</p>
</div>
