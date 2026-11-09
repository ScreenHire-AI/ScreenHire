<script lang="ts">
  import SparklesIcon from '~icons/lucide/sparkles';
  import type { AiAssistanceLevel } from '@screenhire/api-client';
  import Hint from '#lib/components/hint.svelte';
  import { aiAssistanceLevels } from '#lib/verification.js';

  // AI help as a small sparkles mark with a tooltip, outside a badge (design system section 3.2). It is information, never
  // a status, so it never sorts, filters or counts, and it shows only for Some and Strong signs: `level` is whatever
  // `aiMarkOf` returned, and nothing is drawn without one. The tooltip says it is information only (ADR-0004).
  let { level }: { level: AiAssistanceLevel | undefined } = $props();

  const label = $derived(level ? aiAssistanceLevels[level].label : '');
</script>

{#if level && level !== 'NotIndicated'}
  <Hint content={`${label}. Information only: it never changes the verification level.`}>
    {#snippet children({ props })}
      <span {...props} data-ai-mark={level} class="inline-flex min-h-6 items-center text-status-ai-mark">
        <SparklesIcon aria-hidden="true" class="size-3.5" />
        <span class="sr-only">{label}</span>
      </span>
    {/snippet}
  </Hint>
{/if}
