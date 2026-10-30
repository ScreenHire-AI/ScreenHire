<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { LucideIcon } from '#lib/shell.js';

  // One item of a header's meta line (design system S7): an icon and a value that reads on its own ("Applied 2 hours
  // ago via the careers page"), its label only for screen readers. Evidence keeps `Fact`, whose values need their label.
  let { label, icon: Icon, children }: { label: string; icon?: LucideIcon; children: Snippet } = $props();
</script>

<div data-slot="meta-item" class="flex min-w-0 items-center">
  <dt class="sr-only">{label}</dt>
  <dd class="flex min-w-0 items-center gap-1.5">
    {#if Icon}<Icon aria-hidden="true" class="size-4 shrink-0 text-muted-foreground" />{/if}{@render children()}
  </dd>
</div>
