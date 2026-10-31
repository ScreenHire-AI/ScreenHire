<script lang="ts">
  import type { Snippet } from 'svelte';
  import { page } from '$app/state';
  import { visibleSettingsSections } from '#lib/settings.js';

  // The Settings menu (design system section 11): Settings is one sidebar item, and its pages share this menu. From `lg`
  // the menu is a 200px column on the left of the page's content, under the header band; below `lg` it is one row above
  // the content that scrolls sideways and never wraps (F5), and brings the current entry into view. It is a surface, like
  // any panel (S3). The page's content goes in `children`. A record under a settings page (a webhook endpoint) is a
  // detail page, which has no menu. It lists only the pages the person's role may use, as the sidebar does, in their
  // groups: from `lg` each group's name heads it as a caption, as Saved views does in the sidebar; in the row below `lg`
  // a thin rule parts the groups and the names are for screen readers only.
  let {
    children,
    current = page.url.pathname,
  }: {
    children: Snippet;
    /** The path whose entry is marked current; the address by default. */
    current?: string;
  } = $props();

  const sections = $derived(visibleSettingsSections(page.data?.user));
  const groups = $derived(
    [...new Set(sections.map((section) => section.group))].map((group) => ({ group, sections: sections.filter((section) => section.group === group) })),
  );
  const uid = $props.id();

  let list = $state<HTMLDivElement>();
  // Below lg the row may hide the current entry past its edge: scroll the row, never the page, to centre it. From lg the
  // row is a column that does not scroll, so this changes nothing there.
  $effect(() => {
    const entry = list?.querySelector(`a[href="${current}"]`)?.parentElement;
    if (list && entry) list.scrollLeft = entry.offsetLeft - (list.clientWidth - entry.offsetWidth) / 2;
  });
</script>

<div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6 lg:grid lg:grid-cols-[12.5rem_minmax(0,1fr)] lg:items-start lg:gap-6">
  <nav aria-label="Settings" class="min-w-0 rounded-xl border border-border bg-card p-1.5 shadow-surface">
    <div bind:this={list} class="relative flex items-center gap-0.5 overflow-x-auto [scrollbar-width:thin] lg:flex-col lg:items-stretch lg:gap-3 lg:overflow-visible">
      {#each groups as { group, sections: entries }, index (group)}
        {#if index > 0}<span aria-hidden="true" class="mx-1.5 h-5 w-px shrink-0 bg-border lg:hidden"></span>{/if}
        <div class="flex shrink-0 flex-col">
          <p id={`${uid}-${group}`} class="sr-only lg:not-sr-only lg:px-3 lg:pt-1.5 lg:pb-1 lg:text-caption lg:text-muted-foreground">{group}</p>
          <ul aria-labelledby={`${uid}-${group}`} class="flex gap-0.5 lg:flex-col">
      {#each entries as section (section.href)}
        {@const Icon = section.icon}
        <li class="shrink-0">
          <a
            href={section.href}
            aria-current={section.href === current ? 'page' : undefined}
            class={[
              'flex h-9 items-center gap-2 rounded-lg px-3 whitespace-nowrap text-foreground no-underline hover:bg-secondary hover:underline pointer-coarse:h-11',
              section.href === current && 'bg-accent font-semibold text-accent-foreground hover:bg-accent',
            ]}
          >
            <Icon aria-hidden="true" class="shrink-0 text-muted-foreground" />
            {section.label}
          </a>
        </li>
      {/each}
          </ul>
        </div>
      {/each}
    </div>
  </nav>
  <div class="flex min-w-0 flex-col gap-5">{@render children()}</div>
</div>
