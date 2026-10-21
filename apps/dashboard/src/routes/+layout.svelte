<script lang="ts">
  import '../app.css';
  // Applies the saved light or dark theme on every page, sign-in included.
  import '#lib/preferences.svelte.js';
  import { setLucideProps } from '@lucide/svelte/context';
  import type { Snippet } from 'svelte';

  let { children }: { children: Snippet } = $props();

  // Every Lucide icon's size and stroke, set once (design system section 14).
  setLucideProps({ size: 16, strokeWidth: 1.75 });
</script>

{@render children()}
