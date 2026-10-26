<script lang="ts" module>
  /** One thing behind a count, and where it lives. */
  export interface CountItem {
    id: string;
    label: string;
    href: string;
  }
</script>

<script lang="ts">
  import SearchField from '#lib/components/search-field.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';

  // A count in a header (design system T4, section 11), such as "2 hiring templates": an outline button that opens a
  // sheet listing each item as a link, with search once there are more than 10. Design for the count, not the example
  // (principle 11). With nothing to list it is plain text, because there is nothing to open.
  let {
    items,
    one,
    other,
    title,
    description,
    empty,
  }: {
    items: readonly CountItem[];
    /** The noun for one item ("hiring template"). */
    one: string;
    /** The noun for several ("hiring templates"). */
    other: string;
    /** The sheet's title ("Used by"). */
    title: string;
    /** The sheet's one line: usually the record's name. */
    description: string;
    /** What it says with nothing to list, to match a list's column ("None"); the count ("0 jobs") by default. */
    empty?: string;
  } = $props();

  /** Past this many items the sheet offers a search. */
  const searchFrom = 11;

  let open = $state(false);
  let query = $state('');
  const label = $derived(`${items.length} ${items.length === 1 ? one : other}`);
  const words = $derived(query.toLocaleLowerCase().split(/\s+/).filter(Boolean));
  const shown = $derived(words.length === 0 ? items : items.filter((item) => words.every((word) => item.label.toLocaleLowerCase().includes(word))));
</script>

{#if items.length === 0}
  <span>{empty ?? label}</span>
{:else}
  <Button variant="outline" class="tabular-nums" onclick={() => (open = true)}>{label}</Button>
  <Sheet.Root bind:open onOpenChange={(next) => next || (query = '')}>
    <Sheet.Content side="right" class="w-full gap-0 overflow-y-auto data-[side=right]:sm:max-w-md">
      <Sheet.Header class="border-b border-border-subtle">
        <Sheet.Title>{title}</Sheet.Title>
        <Sheet.Description>{description}</Sheet.Description>
      </Sheet.Header>
      {#if items.length >= searchFrom}
        <div class="border-b border-border-subtle p-4">
          <SearchField bind:value={query} label={`Search ${other}`} />
        </div>
      {/if}
      {#if shown.length === 0}
        <p role="status" class="p-4 text-muted-foreground">No matches</p>
      {:else}
        <ul aria-label={label} class="flex flex-col">
          {#each shown as item (item.id)}
            <li class="flex min-h-11 items-center border-b border-border-subtle px-4 py-2.5 last:border-b-0">
              <!-- A link goes somewhere (A5): to the item, closing the sheet on the way. -->
              <TruncatedText text={item.label} href={item.href} underlined onclick={() => (open = false)} />
            </li>
          {/each}
        </ul>
      {/if}
    </Sheet.Content>
  </Sheet.Root>
{/if}
