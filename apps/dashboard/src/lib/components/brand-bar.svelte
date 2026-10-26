<script lang="ts">
  import LogoMark from '#lib/components/logo-mark.svelte';
</script>

<!-- The top bar of a page outside the app shell (sign-in, onboarding, errors): the logo mark and name, nothing else
     (design system section 7). Signed-in pages use TopBar. -->
<header data-slot="brand-bar" class="flex h-14 shrink-0 items-center gap-2.5 border-b border-border bg-card px-4 md:px-5">
  <LogoMark />
  <span class="font-display text-heading font-bold">ScreenHire</span>
</header>
