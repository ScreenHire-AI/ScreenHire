<script lang="ts">
  import type { Snippet } from 'svelte';

  // One section of the details rail: a small heading, then its values. Labels and values use `SummaryList`, which puts
  // each label above its value in the rail's 340px.
  let { title, children }: { title: string; children: Snippet } = $props();

  const id = $props.id();
</script>

<section aria-labelledby={id} class="flex min-w-0 flex-col gap-3 border-b border-border-subtle px-4.5 py-4 last:border-b-0 max-md:px-4">
  <h2 {id} class="text-caption font-semibold text-muted-foreground">{title}</h2>
  {@render children()}
</section>
