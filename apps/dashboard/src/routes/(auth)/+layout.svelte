<script lang="ts">
  import type { Snippet } from 'svelte';

  let { children }: { children: Snippet } = $props();
</script>

<!-- Sign-in and onboarding: one card on the paper background, no app navigation. -->
<div class="flex min-h-screen flex-col items-center gap-6 px-4 py-12 sm:py-20">
  <div class="flex items-center gap-2.5 text-foreground">
    <span class="grid size-8 place-items-center rounded-lg bg-foreground font-display font-extrabold text-background" aria-hidden="true">S</span>
    <span class="font-display text-[19px] font-bold tracking-tight">ScreenHire</span>
  </div>
  <main class="w-full max-w-[400px] rounded-xl border border-border bg-card px-6 py-6">
    {@render children()}
  </main>
</div>
