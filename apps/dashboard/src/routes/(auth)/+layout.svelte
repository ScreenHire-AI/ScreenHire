<script lang="ts">
  import type { Snippet } from 'svelte';
  import BrandBar from '#lib/components/brand-bar.svelte';

  let { children }: { children: Snippet } = $props();
</script>

<!-- Sign-in and onboarding: the brand bar, then each page's own main, with no app navigation. -->
<div class="flex min-h-screen flex-col">
  <BrandBar />
  {@render children()}
</div>
