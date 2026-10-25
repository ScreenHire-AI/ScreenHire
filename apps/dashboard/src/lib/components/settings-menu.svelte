<script lang="ts">
  import type { Snippet } from 'svelte';
  import { page } from '$app/state';
  import { settingsSectionFor, settingsSections } from '#lib/settings.js';

  // The Settings menu (design system section 11): Settings is one sidebar item, and its pages share this menu. From `lg`
  // the menu is a 200px column on the left of the page's content, under the header band; below `lg` it is one row above
  // the content that scrolls sideways and never wraps (F5). It is a surface, like any panel (S3). The page's content goes
  // in `children`.
  let {
    children,
    current = page.url.pathname,
  }: {
    children: Snippet;
    /** The path whose entry is marked current; the address by default. */
    current?: string;
  } = $props();

  const active = $derived(settingsSectionFor(current)?.href);
</script>

<div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6 lg:grid lg:grid-cols-[12.5rem_minmax(0,1fr)] lg:items-start lg:gap-6">
  <nav aria-label="Settings" class="min-w-0 rounded-xl border border-border bg-card p-1.5 shadow-surface">
    <ul class="flex gap-0.5 overflow-x-auto lg:flex-col lg:overflow-visible">
      {#each settingsSections as section (section.href)}
        {@const Icon = section.icon}
        <li class="shrink-0">
          <a
            href={section.href}
            aria-current={section.href === active ? 'page' : undefined}
            class={[
              'flex h-9 items-center gap-2 rounded-lg px-3 whitespace-nowrap text-foreground no-underline hover:bg-secondary hover:underline pointer-coarse:h-11',
              section.href === active && 'bg-accent font-semibold text-accent-foreground hover:bg-accent',
            ]}
          >
            <Icon aria-hidden="true" class="shrink-0 text-muted-foreground" />
            {section.label}
          </a>
        </li>
      {/each}
    </ul>
  </nav>
  <div class="flex min-w-0 flex-col gap-5">{@render children()}</div>
</div>
