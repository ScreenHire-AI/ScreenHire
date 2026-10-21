<script lang="ts">
  import SearchIcon from '@lucide/svelte/icons/search';
  import { cn } from '#lib/utils.js';

  // A list's search box, in its table panel's header (design system section 7). The page filters the rows it passes to
  // the table, so the count matches what is shown. 16px text on touch, so phones do not zoom in (F4).
  let { value = $bindable(''), label, class: className }: { value?: string; label: string; class?: string } = $props();
</script>

<label
  class={cn(
    'flex h-8 min-w-0 items-center gap-2 rounded-lg border border-input bg-card px-2.5 text-muted-foreground focus-within:border-ring focus-within:ring-3 focus-within:ring-ring/50 pointer-coarse:h-11',
    className,
  )}
>
  <SearchIcon aria-hidden="true" class="shrink-0" />
  <span class="sr-only">{label}</span>
  <input
    type="search"
    bind:value
    placeholder={label}
    class="h-full min-w-0 flex-1 bg-transparent text-body text-foreground outline-none placeholder:text-muted-foreground pointer-coarse:text-reading"
  />
</label>
