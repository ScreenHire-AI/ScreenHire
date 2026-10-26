<script lang="ts" generics="T extends string">
  // A choice card (design system section 11): one of a few options, a bordered card with its name and one muted line.
  // The chosen card takes the primary edge and the accent fill, and the whole card is the target. Put the cards of one
  // choice in a `role="radiogroup"` with its label.
  let {
    name,
    value,
    group = $bindable(),
    title,
    description,
  }: {
    name: string;
    value: T;
    group: T | undefined;
    title: string;
    description?: string;
  } = $props();
</script>

<label
  class="flex cursor-pointer items-start gap-3 rounded-lg border border-input p-3 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring"
>
  <input type="radio" {name} {value} bind:group class="mt-0.5 size-4 shrink-0 accent-primary" />
  <span class="flex min-w-0 flex-col gap-0.5">
    <span class="font-semibold break-words">{title}</span>
    {#if description}<span class="text-muted-foreground">{description}</span>{/if}
  </span>
</label>
