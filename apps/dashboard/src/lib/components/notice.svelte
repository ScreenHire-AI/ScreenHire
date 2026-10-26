<script lang="ts">
  import type { Snippet } from 'svelte';
  import ProblemIcon from '#lib/components/problem-icon.svelte';
  import { cn } from '#lib/utils.js';

  // A problem to fix (design system E2, section 11), on the page where people will see it: the verify status's soft
  // colour, full width, what is wrong in words, and a link to where it is fixed. It starts with the problem icon, in the
  // verify colour as everywhere (section 14), never a status icon (I2). A notice is not a status badge and never stands in
  // for one.
  let {
    href,
    link,
    children,
    class: className,
  }: {
    /** Where the fix is: another tab, page or record (A5). */
    href?: string;
    /** The link's words, such as "Fix on Distribution". */
    link?: string;
    /** What is wrong, in words. */
    children: Snippet;
    class?: string;
  } = $props();
</script>

<div role="status" data-slot="notice" class={cn('flex w-full items-start gap-3 rounded-panel bg-status-verify-soft px-3.5 py-3 text-status-verify', className)}>
  <ProblemIcon class="mt-0.5" />
  <div class="flex min-w-0 flex-1 flex-wrap items-baseline gap-x-4 gap-y-1">
    <p class="min-w-0 flex-[1_1_16rem]">{@render children()}</p>
    {#if href && link}
      <a {href} class="inline-flex min-h-6 shrink-0 items-center font-semibold underline underline-offset-2 pointer-coarse:min-h-11">{link}</a>
    {/if}
  </div>
</div>
