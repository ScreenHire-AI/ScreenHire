<script lang="ts" module>
  import type { SignalEvidence } from '@screenhire/api-client';

  /** Evidence that needs the full width (a quote, a comparison, a list); the rest is a fact in a facts row. */
  export const isWideEvidence = (evidence: SignalEvidence) =>
    evidence.kind === 'Quote' || evidence.kind === 'TextComparison' || evidence.kind === 'List';
</script>

<script lang="ts">
  import Fact from '#lib/components/fact.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { locales } from '#lib/dates.js';

  // One piece of evidence, rendered by its kind rather than by the signal it belongs to, so a new signal needs no
  // dashboard code (docs/architecture/verification-engine.md, section 10.1). A metric is a measurement next to its
  // reference, never a likelihood. With `fact`, a short one is a `Fact` in the caller's facts row (`<dl>`), the same
  // label-above-value pattern as a header's facts (section 7), so a reason's evidence reads across; a quote, a
  // comparison or a list is a block with its label above it either way.
  let { evidence, fact = false }: { evidence: SignalEvidence; fact?: boolean } = $props();

  const number = new Intl.NumberFormat(locales, { maximumFractionDigits: 1 });
  const measure = (amount: number, unit: string | null | undefined) => (unit ? `${number.format(amount)} ${unit}` : number.format(amount));
  const wide = $derived(isWideEvidence(evidence));
</script>

{#snippet value()}
  {#if evidence.kind === 'Fact'}
    <span class="break-words">{evidence.value}</span>
  {:else if evidence.kind === 'Quote'}
    <blockquote class="max-w-measure rounded-md border border-border-subtle bg-card px-2.5 py-1.5 whitespace-pre-line break-words">{evidence.text}</blockquote>
    {#if evidence.source}<p class="mt-1 text-caption text-muted-foreground">{evidence.source}</p>{/if}
  {:else if evidence.kind === 'TextComparison'}
    <div class="grid grid-cols-1 gap-1.5 @sm:grid-cols-2">
      {#each [evidence.left, evidence.right] as side, index (index)}
        {#if side}
          <div class="flex min-w-0 flex-col gap-0.5 rounded-md border border-border-subtle bg-card px-2.5 py-1.5">
            <p class="text-caption text-muted-foreground">{side.label}</p>
            <p class="whitespace-pre-line break-words">{side.text}</p>
          </div>
        {/if}
      {/each}
    </div>
  {:else if evidence.kind === 'Metric'}
    <span class="tabular-nums">{evidence.amount == null ? 'not measured' : measure(evidence.amount, evidence.unit)}</span>
    {#if evidence.reference != null}
      <span class="block text-caption text-muted-foreground">{evidence.referenceLabel ?? 'Reference'}: <span class="tabular-nums">{measure(evidence.reference, evidence.unit)}</span></span>
    {/if}
  {:else if evidence.kind === 'List'}
    <!-- Fact pills sit on white (section 11), so the list gets a white well like a quote's. -->
    <ul class="flex flex-wrap gap-1 rounded-md border border-border-subtle bg-card px-2.5 py-1.5">
      {#each evidence.items ?? [] as item, index (index)}
        <li><Badge variant="muted">{item}</Badge></li>
      {/each}
    </ul>
  {:else}
    <!-- A kind this build does not know yet: still show what was observed, in words. -->
    <span>{evidence.value ?? evidence.text ?? ''}</span>
  {/if}
{/snippet}

{#if fact && !wide}
  <Fact label={evidence.label} data-evidence-kind={evidence.kind}>{@render value()}</Fact>
{:else if wide}
  <figure data-evidence-kind={evidence.kind} class={['flex min-w-0 flex-col gap-1 text-body', evidence.kind === 'TextComparison' && '@container']}>
    <figcaption class="text-caption text-muted-foreground">{evidence.label}</figcaption>
    {@render value()}
  </figure>
{:else}
  <div data-evidence-kind={evidence.kind} class="flex min-w-0 flex-col gap-1 text-body">
    <p><span class="text-muted-foreground">{evidence.label}:</span> {@render value()}</p>
  </div>
{/if}
