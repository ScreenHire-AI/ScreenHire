<script lang="ts">
  import type { Snippet } from 'svelte';
  import BrandBar from '#lib/components/brand-bar.svelte';

  let { children }: { children: Snippet } = $props();
</script>

<!-- Onboarding, invitations and connecting an assistant: the brand bar, then each page's own main, with no app
     navigation. The sign-in pages reset past this layout (`sign-in/+layout@.svelte`): their card carries the mark. -->
<div class="flex min-h-screen flex-col">
  <BrandBar />
  {@render children()}
</div>
