<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';

  // One answer in a guided step (design system section 11, Guided test): a bordered card with its name and one muted line,
  // and the whole card is the button. Pressing it chooses and moves on, so it is a button, not a radio (A5, A6). The
  // answer chosen earlier keeps the `primary` edge and `accent` fill, with `aria-current`, when the step is shown again.
  let {
    title,
    description,
    current = false,
    onclick,
  }: {
    title: string;
    description?: string;
    current?: boolean;
    onclick: () => void;
  } = $props();
</script>

<button
  type="button"
  {onclick}
  aria-current={current ? 'true' : undefined}
  class="flex w-full min-w-0 items-center gap-3 rounded-lg border border-input p-3 text-left hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none aria-[current=true]:border-primary aria-[current=true]:bg-accent pointer-coarse:min-h-11"
>
  <span class="flex min-w-0 flex-1 flex-col gap-0.5">
    <span class="font-semibold break-words">{title}</span>
    {#if description}<span class="text-muted-foreground">{description}</span>{/if}
  </span>
  <ArrowRightIcon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground" />
</button>
