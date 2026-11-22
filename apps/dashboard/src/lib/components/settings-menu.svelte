<script lang="ts">
  import type { Snippet } from 'svelte';
  import { page } from '$app/state';
  import SearchField from '#lib/components/search-field.svelte';
  import SettingMark from '#lib/components/setting-mark.svelte';
  import { searchSettings, settingsGroupOf, settingsGroups, visibleSettingsSections } from '#lib/settings.js';
  import { getShell } from '#lib/shell.js';

  // The Settings menu (design system section 11): every settings page the person's role may use, in its groups
  // (Organization, Careers site, Integrations) under their names, on every settings page, so the whole of Settings is in
  // view wherever someone is in it and nothing hides behind a group. A search at the top narrows it by the same words as
  // Ctrl K. From `lg` the menu is a column on the left of the page's content, under the header band; below `lg` it is one
  // row above the content that scrolls sideways and never wraps (F5), without the group names or the search, and brings
  // the current entry into view. It is a surface, like any panel (S3). The page's content goes in `children`. A record
  // under a settings page (a role, a webhook endpoint) is a detail page, which has no menu. Email domain carries a check
  // once it works and a cross while it needs fixing.
  let {
    children,
    current = page.url.pathname,
  }: {
    children: Snippet;
    /** The path whose entry is marked current; the address by default. */
    current?: string;
  } = $props();

  const shell = getShell();
  const inSettings = $derived(settingsGroupOf(current) !== undefined);
  const visible = $derived(inSettings ? visibleSettingsSections(page.data?.user, shell?.features()) : []);
  let query = $state('');
  const searching = $derived(query.trim() !== '');
  const groups = $derived(settingsGroups(searching ? searchSettings(visible, query) : visible));
  const domainMark = $derived(shell?.sendingDomainMark());

  let list = $state<HTMLDivElement>();
  // Below lg the row may hide the current entry past its edge: scroll the row, never the page, to centre it. From lg the
  // row is a column that does not scroll, so this changes nothing there. It runs again when the entries change: sections
  // behind a feature appear once the shell's queries answer, which moves the current entry after the first centring.
  $effect(() => {
    void domainMark;
    void groups;
    const entry = list?.querySelector(`a[href="${current}"]`)?.parentElement;
    if (list && entry) list.scrollLeft = entry.offsetLeft - (list.clientWidth - entry.offsetWidth) / 2;
  });
</script>

{#if visible.length > 0}
  <div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6 lg:grid lg:grid-cols-[12.5rem_minmax(0,1fr)] lg:items-start lg:gap-6">
    <nav aria-label="Settings" class="min-w-0 rounded-xl border border-border bg-card p-1.5 shadow-surface">
      <div role="search" class="hidden p-1 lg:block">
        <SearchField bind:value={query} label="Search settings" class="h-9 w-full" />
      </div>
      <div bind:this={list} class="relative flex items-center gap-0.5 overflow-x-auto [scrollbar-width:none] lg:flex-col lg:items-stretch lg:overflow-visible">
        {#each groups as { group, sections } (group)}
          {@const headingId = `settings-group-${group.replaceAll(' ', '-').toLowerCase()}`}
          <p id={headingId} class="hidden px-3 pt-3 pb-1 text-caption font-semibold text-muted-foreground lg:block">{group}</p>
          <ul aria-labelledby={headingId} class="flex gap-0.5 lg:w-full lg:flex-col">
            {#each sections as section (section.href)}
              <li class="shrink-0">
                <a
                  href={section.href}
                  aria-current={section.href === current ? 'page' : undefined}
                  class={[
                    // Indented under its group's name from lg, so each group's pages read as its own. 32px high from lg with a mouse, so
                    // the whole menu of an Owner (15 pages) and a page that fits one 1440x900 window, such as Branding, share the screen;
                    // 36px in the row below lg and 44px on touch (design system section 11, Settings menu).
                    'flex h-9 items-center gap-2 rounded-lg px-3 whitespace-nowrap text-foreground no-underline hover:bg-secondary hover:underline pointer-coarse:h-11 lg:pl-6 lg:pointer-fine:h-8',
                    section.href === current && 'bg-accent font-semibold text-accent-foreground hover:bg-accent',
                  ]}
                >
                  {section.label}
                  {#if section.href === '/settings/sending-domain' && domainMark}<SettingMark mark={domainMark} />{/if}
                </a>
              </li>
            {/each}
          </ul>
        {/each}
        {#if searching && groups.length === 0}
          <p role="status" class="hidden px-3 py-2 text-muted-foreground lg:block">No setting matches.</p>
        {/if}
      </div>
    </nav>
    <div class="flex min-w-0 flex-col gap-5">{@render children()}</div>
  </div>
{:else}
  <div class="flex w-full min-w-0 flex-col gap-5 px-4 py-6 md:px-6">{@render children()}</div>
{/if}
