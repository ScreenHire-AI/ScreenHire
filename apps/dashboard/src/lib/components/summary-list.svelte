<script lang="ts">
  import type { Snippet } from 'svelte';

  // Read-only data as labels and values (design system section 11), never disabled inputs. Holds `SummaryItem`s; the
  // label sits beside its value when there is room and above it when there is not.
  let { children }: { children: Snippet } = $props();
</script>

<dl data-slot="summary-list" class="@container flex flex-col">{@render children()}</dl>
