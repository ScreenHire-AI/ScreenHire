<script lang="ts">
  import type { SignalEvidence } from '@screenhire/api-client';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { locales } from '#lib/dates.js';

  // One piece of evidence, rendered by its kind rather than by the signal it belongs to, so a new signal needs no
  // dashboard code (docs/architecture/integrity-engine.md, section 10.1). A metric is a measurement next to its
  // reference, never a likelihood.
  let { evidence }: { evidence: SignalEvidence } = $props();

  const number = new Intl.NumberFormat(locales, { maximumFractionDigits: 1 });
  const measure = (amount: number, unit: string | null | undefined) => (unit ? `${number.format(amount)} ${unit}` : number.format(amount));
</script>

<div data-evidence-kind={evidence.kind} class="flex min-w-0 flex-col gap-1 text-body">
  {#if evidence.kind === 'Fact'}
    <p><span class="text-muted-foreground">{evidence.label}:</span> <span class="font-semibold break-words">{evidence.value}</span></p>
  {:else if evidence.kind === 'Quote'}
    <figure class="flex flex-col gap-1">
      <figcaption class="text-muted-foreground">{evidence.label}</figcaption>
      <blockquote class="max-w-measure rounded-md border border-border-subtle bg-card px-2.5 py-1.5 whitespace-pre-line break-words">{evidence.text}</blockquote>
      {#if evidence.source}<p class="text-caption text-muted-foreground">{evidence.source}</p>{/if}
    </figure>
  {:else if evidence.kind === 'TextComparison'}
    <figure class="@container flex flex-col gap-1">
      <figcaption class="text-muted-foreground">{evidence.label}</figcaption>
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
    </figure>
  {:else if evidence.kind === 'Metric'}
    <p>
      <span class="text-muted-foreground">{evidence.label}:</span>
      <span class="font-semibold tabular-nums">{evidence.amount == null ? 'not measured' : measure(evidence.amount, evidence.unit)}</span>
      {#if evidence.reference != null}
        <span class="block text-muted-foreground">{evidence.referenceLabel ?? 'Reference'}: <span class="tabular-nums">{measure(evidence.reference, evidence.unit)}</span></span>
      {/if}
    </p>
  {:else if evidence.kind === 'List'}
    <p class="text-muted-foreground">{evidence.label}</p>
    <!-- Fact pills sit on white (section 11), so the list gets a white well like a quote's. -->
    <ul class="flex flex-wrap gap-1 rounded-md border border-border-subtle bg-card px-2.5 py-1.5">
      {#each evidence.items ?? [] as item, index (index)}
        <li><Badge variant="muted">{item}</Badge></li>
      {/each}
    </ul>
  {:else}
    <!-- A kind this build does not know yet: still show what was observed, in words. -->
    <p><span class="text-muted-foreground">{evidence.label}:</span> {evidence.value ?? evidence.text ?? ''}</p>
  {/if}
</div>
