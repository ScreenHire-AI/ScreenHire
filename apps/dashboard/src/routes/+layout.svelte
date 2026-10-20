<script lang="ts">
  import '../app.css';
  // Applies the saved light or dark theme on every page, sign-in included.
  import '#lib/preferences.svelte.js';
  import type { Snippet } from 'svelte';

  let { children }: { children: Snippet } = $props();
</script>

{@render children()}
