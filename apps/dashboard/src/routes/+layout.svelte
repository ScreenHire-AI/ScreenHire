<script lang="ts">
  import '../app.css';
  // Applies the saved light or dark theme on every page, sign-in included.
  import '#lib/preferences.svelte.js';
  import type { Snippet } from 'svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';

  let { children }: { children: Snippet } = $props();
</script>

<!-- Every tooltip needs a provider; one here lets any page use them. -->
<Tooltip.Provider>
  {@render children()}
</Tooltip.Provider>
