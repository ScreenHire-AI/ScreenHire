<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';

  // One section of the details rail: a small heading, then its values. Labels and values use `SummaryList`, which puts
  // each label above its value in the rail's 340px. A section listing things that grow (a webhook's events) counts them
  // in the count badge after its heading (T4).
  let { title, count, children }: { title: string; count?: number; children: Snippet } = $props();

  const id = $props.id();
</script>

<section aria-labelledby={id} class="flex min-w-0 flex-col gap-3 border-b border-border-subtle px-4.5 py-4 last:border-b-0 max-md:px-4">
  <h2 {id} class="flex items-center gap-2 text-caption font-semibold text-muted-foreground">
    {title}{#if count !== undefined}<Badge variant="count">{count}</Badge>{/if}
  </h2>
  {@render children()}
</section>
