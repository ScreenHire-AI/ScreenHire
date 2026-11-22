<script lang="ts">
  import type { Snippet } from 'svelte';
  import { page } from '$app/state';
  import { accountSections } from '#lib/account.js';

  // The Your account menu (design system section 21, "Account pages"): the Settings menu's layout for the person's own
  // pages, a 200px column left of the content from `lg` and one row that scrolls sideways below it. Five entries at most and
  // no groups, so no search (S9 asks for one past about ten). The page's content goes in `children`.
  let {
    children,
    current = page.url.pathname,
  }: {
    children: Snippet;
    /** The path whose entry is marked current; the address by default. */
    current?: string;
  } = $props();

  const sections = accountSections;

  let list = $state<HTMLUListElement>();
  // Below lg the row may hide the current entry past its edge: scroll the row, never the page, to centre it.
  $effect(() => {
    const entry = list?.querySelector(`a[href="${current}"]`)?.parentElement;
    if (list && entry) list.scrollLeft = entry.offsetLeft - (list.clientWidth - entry.offsetWidth) / 2;
  });
</script>

<div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6 lg:grid lg:grid-cols-[12.5rem_minmax(0,1fr)] lg:items-start lg:gap-6">
  <nav aria-label="Your account" class="min-w-0 rounded-xl border border-border bg-card p-1.5 shadow-surface">
    <ul bind:this={list} class="relative flex gap-0.5 overflow-x-auto [scrollbar-width:none] lg:flex-col lg:overflow-visible">
      {#each sections as section (section.href)}
        <li class="shrink-0">
          <a
            href={section.href}
            aria-current={section.href === current ? 'page' : undefined}
            class={[
              'flex h-9 items-center rounded-lg px-3 whitespace-nowrap text-foreground no-underline hover:bg-secondary hover:underline pointer-coarse:h-11',
              section.href === current && 'bg-accent font-semibold text-accent-foreground hover:bg-accent',
            ]}
          >
            {section.label}
          </a>
        </li>
      {/each}
    </ul>
  </nav>
  <div class="flex min-w-0 flex-col gap-5">{@render children()}</div>
</div>
