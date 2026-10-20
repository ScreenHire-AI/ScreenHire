<script lang="ts">
  import MoonIcon from '@lucide/svelte/icons/moon';
  import SearchIcon from '@lucide/svelte/icons/search';
  import SunIcon from '@lucide/svelte/icons/sun';
  import { preferences } from '#lib/preferences.svelte.js';
  import { getShell } from '#lib/shell.js';

  // The bar above every signed-in page: where you are, the command palette and the theme switch.
  let { crumbs }: { crumbs: readonly { label: string; href?: string }[] } = $props();

  const shell = getShell();
  const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);
</script>

<header class="flex flex-wrap items-center gap-2.5 border-b border-border bg-card px-5 py-2.5">
  <nav aria-label="Breadcrumb" class="min-w-0">
    <ol class="flex flex-wrap items-center gap-1.5 text-[13px] text-muted-foreground">
      {#each crumbs as crumb, index (index)}
        <li class="flex items-center gap-1.5">
          {#if index > 0}<span aria-hidden="true">/</span>{/if}
          {#if crumb.href && index < crumbs.length - 1}
            <a href={crumb.href} class="text-muted-foreground no-underline hover:underline">{crumb.label}</a>
          {:else}
            <span aria-current={index === crumbs.length - 1 ? 'page' : undefined} class="font-bold text-foreground">{crumb.label}</span>
          {/if}
        </li>
      {/each}
    </ol>
  </nav>
  <div class="flex-1"></div>
  <button
    type="button"
    onclick={() => shell.openPalette()}
    aria-keyshortcuts={isMac ? 'Meta+K' : 'Control+K'}
    class="flex h-[34px] w-[300px] max-w-full items-center gap-2 rounded-[9px] border border-border bg-muted px-3 text-left text-muted-foreground hover:border-input"
  >
    <SearchIcon class="size-4" aria-hidden="true" />
    <span class="flex-1">Search or jump to…</span>
    <kbd class="font-mono text-[11px]">{isMac ? '⌘K' : 'Ctrl K'}</kbd>
  </button>
  <button
    type="button"
    onclick={() => preferences.toggleTheme()}
    aria-label={preferences.theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
    class="grid size-[34px] place-items-center rounded-[9px] border border-border bg-card text-secondary-foreground hover:bg-secondary"
  >
    {#if preferences.theme === 'dark'}<SunIcon class="size-4" aria-hidden="true" />{:else}<MoonIcon class="size-4" aria-hidden="true" />{/if}
  </button>
</header>
