<script lang="ts">
  import { Button } from '#lib/components/ui/button/index.js';

  // The foot of a phone's list that lists a page of its items at a time (design system S9): how many are shown of how
  // many there are, and the button that shows the next page. A table on a larger window needs none, because its rows are
  // virtual or its panel scrolls.
  let { shown, total, onmore }: { shown: number; total: number; onmore: () => void } = $props();
</script>

{#if total > shown}
  <div class="flex flex-col items-center gap-2 border-t border-border-subtle p-4">
    <p class="text-muted-foreground" role="status">Showing {shown} of {total}</p>
    <Button variant="outline" size="lg" onclick={onmore}>Show more</Button>
  </div>
{/if}
