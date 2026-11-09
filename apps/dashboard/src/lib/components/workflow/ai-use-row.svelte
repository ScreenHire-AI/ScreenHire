<script lang="ts">
  import type { AiUsePolicy } from '@screenhire/api-client';
  import LockIcon from '~icons/lucide/lock';
  import { aiUseName } from '#lib/workflow.js';

  // The AI-use question as the Questions tab shows it: a locked row, because ScreenHire asks it on every application and
  // the policy is the organization's (Verification), which a job may override in its own settings. No automation reads
  // the answer. `href` is where the policy is changed, left out for someone who cannot change it (A3); `policy` is
  // undefined while it loads.
  let { policy, href }: { policy?: AiUsePolicy; href?: string } = $props();
</script>

<div data-ai-use-row class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 rounded-lg bg-muted px-3 py-2">
  <LockIcon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground" />
  <p class="min-w-0 flex-1">
    <span class="font-semibold">AI use.</span> Asked by ScreenHire.{#if policy}{' '}Policy: {aiUseName(policy)}.{/if}
  </p>
  {#if href}
    <a {href} class="inline-flex min-h-6 shrink-0 items-center font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11">Change policy</a>
  {/if}
</div>
