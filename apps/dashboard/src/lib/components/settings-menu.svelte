<script lang="ts">
  import type { Snippet } from 'svelte';
  import { page } from '$app/state';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { settingsGroupOf, visibleSettingsSections } from '#lib/settings.js';
  import { getShell } from '#lib/shell.js';

  // The Settings menu (design system section 11): every page in a group (Organization, Careers site, Integrations) shares
  // this menu, which lists only that group's pages. From `lg` the menu is a 200px column on the left of
  // the page's content, under the header band; below `lg` it is one row above the content that scrolls sideways and
  // never wraps (F5), and brings the current entry into view. It is a surface, like any panel (S3). The page's content
  // goes in `children`. A record under a settings page (a role, a webhook endpoint) is a detail page, which has no menu.
  // It lists only the pages the person's role may use, as the Settings home does, each by its name alone. Email domain carries
  // its status while something is left to do.
  let {
    children,
    current = page.url.pathname,
  }: {
    children: Snippet;
    /** The path whose entry is marked current; the address by default. */
    current?: string;
  } = $props();

  const shell = getShell();
  const group = $derived(settingsGroupOf(current));
  const entries = $derived(
    group === undefined ? [] : visibleSettingsSections(page.data?.user, shell?.features()).filter((section) => section.group === group),
  );
  const domainMark = $derived(shell?.sendingDomainMark());

  let list = $state<HTMLDivElement>();
  // Below lg the row may hide the current entry past its edge: scroll the row, never the page, to centre it. From lg the
  // row is a column that does not scroll, so this changes nothing there.
  // Email domain's badge arrives with the shell's query and widens its entry, so the row centres again when it does.
  $effect(() => {
    void domainMark;
    const entry = list?.querySelector(`a[href="${current}"]`)?.parentElement;
    if (list && entry) list.scrollLeft = entry.offsetLeft - (list.clientWidth - entry.offsetWidth) / 2;
  });
</script>

{#if entries.length > 0}
  <div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6 lg:grid lg:grid-cols-[12.5rem_minmax(0,1fr)] lg:items-start lg:gap-6">
    <nav aria-label={group} class="min-w-0 rounded-xl border border-border bg-card p-1.5 shadow-surface">
      <div bind:this={list} class="relative flex items-center gap-0.5 overflow-x-auto [scrollbar-width:none] lg:flex-col lg:items-stretch lg:overflow-visible">
        <ul class="flex gap-0.5 lg:w-full lg:flex-col">
          {#each entries as section (section.href)}
            <li class="shrink-0">
              <a
                href={section.href}
                aria-current={section.href === current ? 'page' : undefined}
                class={[
                  'flex h-9 items-center gap-2 rounded-lg px-3 whitespace-nowrap text-foreground no-underline hover:bg-secondary hover:underline pointer-coarse:h-11',
                  section.href === current && 'bg-accent font-semibold text-accent-foreground hover:bg-accent',
                // In the 200px column the badge wraps under the name rather than overflowing; the row below lg never wraps (F5).
                section.href === '/settings/sending-domain' && domainMark && 'lg:h-auto lg:min-h-9 lg:flex-wrap lg:py-1',
                ]}
              >
                {section.label}
                {#if section.href === '/settings/sending-domain' && domainMark}
                  <StatusBadge {...domainMark} />
                {/if}
              </a>
            </li>
          {/each}
        </ul>
      </div>
    </nav>
    <div class="flex min-w-0 flex-col gap-5">{@render children()}</div>
  </div>
{:else}
  <div class="flex w-full min-w-0 flex-col gap-5 px-4 py-6 md:px-6">{@render children()}</div>
{/if}
