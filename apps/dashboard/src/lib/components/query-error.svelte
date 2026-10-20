<script lang="ts">
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // A request that failed: what went wrong, in a sentence, and a way to try again. Never the raw response.
  let { message, onretry }: { message: string; onretry: () => unknown } = $props();
</script>

<div role="alert" class="flex flex-wrap items-center gap-3 rounded-xl border border-border bg-card px-4 py-3">
  <StatusMark shape="square" tone="verify" />
  <p class="flex-1 text-status-verify">{message}</p>
  <Button variant="outline" size="sm" onclick={() => onretry()}>Try again</Button>
</div>
