<script lang="ts">
  import type { Snippet } from 'svelte';
  import { cn } from '#lib/utils.js';

  // A mini card (design system section 11): a small bordered card in a record's header, a caption title and two lines of
  // facts. It reads on its own, so it is a group named by its title. It is not a panel (S3) and never a link as a whole (A5):
  // a link inside it is a link in its text.
  let { title, children, class: className }: { title: string; children: Snippet; class?: string } = $props();

  const titleId = $props.id();
</script>

<div role="group" aria-labelledby={titleId} class={cn('flex min-w-0 flex-col gap-1.5 rounded-panel border border-border bg-card px-3.5 py-3', className)}>
  <p id={titleId} class="text-caption font-semibold text-muted-foreground">{title}</p>
  <div class="flex min-w-0 flex-col gap-1">{@render children()}</div>
</div>
