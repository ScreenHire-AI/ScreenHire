<script lang="ts">
  import type { VerificationSummary } from '@screenhire/api-client';
  import AiMark from '#lib/components/ai-mark.svelte';
  import VerificationChip from '#lib/components/verification-chip.svelte';
  import { aiMarkOf, stateOf } from '#lib/verification.js';

  // A candidate list's Verification cell, and the phone list item's status: the one-word state with the AI mark beside it
  // for Some and Strong signs (design system section 21). A cell holds one thing, so nothing else sits in it (L1).
  let { summary }: { summary: VerificationSummary | null | undefined } = $props();

  const state = $derived(stateOf(summary));
  const reasons = $derived(summary ? summary.authenticityReasons.length : 0);
</script>

<span class="inline-flex items-center gap-2">
  <VerificationChip {state} {reasons} />
  <AiMark level={aiMarkOf(summary)} />
</span>
