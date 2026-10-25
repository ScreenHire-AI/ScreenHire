<script lang="ts">
  import ReorderList from '#lib/components/reorder-list.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  // Two reorder lists on one page, each owning its order, as a builder with nested lists would.
  let {
    first = ['Your CV', 'Right to work', 'Notice period'],
    second = ['Morning', 'Afternoon'],
    onreorder,
  }: { first?: string[]; second?: string[]; /** Each `onreorder` call, with the list's label. */ onreorder?: (label: string, next: string[]) => void } =
    $props();
  // svelte-ignore state_referenced_locally: the harness starts from its props and then owns the order.
  let questions = $state([...first]);
  // svelte-ignore state_referenced_locally: as above.
  let slots = $state([...second]);
</script>

<Tooltip.Provider>
  {#each [{ label: 'Questions', get: () => questions, set: (next: string[]) => (questions = next) }, { label: 'Slots', get: () => slots, set: (next: string[]) => (slots = next) }] as list (list.label)}
    <ReorderList
      items={list.get()}
      key={(value) => value}
      label={list.label}
      itemLabel={(value) => value}
      onreorder={(next) => {
        onreorder?.(list.label, next);
        list.set(next);
      }}
    >
      {#snippet item(value, grip)}<div class="flex items-center gap-2">{@render grip()}<span>{value}</span></div>{/snippet}
    </ReorderList>
  {/each}
</Tooltip.Provider>
