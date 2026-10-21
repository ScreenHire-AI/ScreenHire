<script lang="ts">
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // A request that failed: what went wrong, in a sentence, and a way to try again. Never the raw response. On the page it
  // is its own surface; inside a panel, sheet or dialog it is an inset, so it is never a card inside a card (S4).
  let { message, onretry, inset = false }: { message: string; onretry: () => unknown; inset?: boolean } = $props();
</script>

<div
  role="alert"
  data-slot="query-error"
  class={[
    'flex flex-wrap items-center gap-3 px-4 py-3',
    inset ? 'rounded-lg bg-secondary' : 'rounded-panel border border-border bg-card',
  ]}
>
  <StatusMark shape="square" tone="verify" />
  <p class="flex-[1_1_12rem] text-status-verify">{message}</p>
  <Button variant="outline" size="lg" onclick={() => onretry()}>Try again</Button>
</div>
