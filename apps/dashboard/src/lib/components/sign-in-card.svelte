<script lang="ts">
  import type { Snippet } from 'svelte';
  import LogoMark from '#lib/components/logo-mark.svelte';

  // A sign-in page (design system section 7): one centred card on the page background, with the mark, the page's one h1
  // and an optional plain line under it, then the page's controls. The card carries the mark, so these pages have no
  // brand bar, and nothing readable sits on the page background outside it (S3). The card stops at 416px and keeps a 16px
  // gutter on a phone; it centres in the window when there is room and starts at the top when there is not.
  let {
    title,
    description,
    heading = $bindable(),
    children,
  }: {
    title: string;
    /** One plain line under the title. */
    description?: string;
    /** The h1, for a page that moves focus to its title when the title changes in place (WCAG 2.4.3). */
    heading?: HTMLHeadingElement;
    children: Snippet;
  } = $props();
</script>

<main class="flex min-h-dvh flex-col items-center px-4 py-10 md:py-16">
  <div data-slot="sign-in-card" class="my-auto flex w-full max-w-104 flex-col gap-6 rounded-panel border border-border bg-card p-6 shadow-surface md:p-8">
    <div class="flex flex-col items-center gap-4 text-center">
      <LogoMark size="large" />
      <div class="flex flex-col gap-1.5">
        <h1 bind:this={heading} tabindex="-1" class="font-display text-page-title text-balance break-words">{title}</h1>
        {#if description}
          <p class="text-muted-foreground">{description}</p>
        {/if}
      </div>
    </div>
    <div class="flex flex-col gap-5">
      {@render children()}
    </div>
  </div>
</main>
