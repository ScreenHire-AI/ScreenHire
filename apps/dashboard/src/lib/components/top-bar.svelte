<script lang="ts">
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import KeyboardIcon from '@lucide/svelte/icons/keyboard';
  import MoonIcon from '@lucide/svelte/icons/moon';
  import SearchIcon from '@lucide/svelte/icons/search';
  import SunIcon from '@lucide/svelte/icons/sun';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Kbd, KbdGroup } from '#lib/components/ui/kbd/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { getShell } from '#lib/shell.js';

  // The bar above every signed-in page: where you are, search (the command palette), the shortcuts dialog and the theme
  // switch. On a phone it starts with the menu button, or with Back on a detail page (design system section 8).
  let {
    crumbs,
    back = false,
  }: {
    crumbs: readonly { label: string; href?: string }[];
    /** A detail page: phones show Back, to the breadcrumb's level up, in place of the menu button. */
    back?: boolean;
  } = $props();

  const up = $derived(back ? crumbs.slice(0, -1).findLast((crumb) => crumb.href) : undefined);

  const shell = getShell();
  // The sidebar's own phone test (under md), so the bar and the sheet switch together.
  const sidebar = Sidebar.useSidebar();
  const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);
  const paletteKeys = isMac ? ['⌘', 'K'] : ['Ctrl', 'K'];
</script>

<header class="flex h-14 shrink-0 items-center gap-2 border-b border-border bg-card px-4 md:px-5">
  {#if sidebar.isMobile}
    {#if up?.href}
      <Button href={up.href} variant="ghost" size="lg" aria-label={`Back to ${up.label}`} class="-ml-2">
        <ArrowLeftIcon aria-hidden="true" />
        Back
      </Button>
    {:else}
      <Sidebar.Trigger size="icon-lg" class="-ml-1.5" />
    {/if}
  {/if}

  <nav aria-label="Breadcrumb" class="min-w-0 flex-1">
    <ol class="flex min-w-0 items-center gap-1.5 text-body text-muted-foreground">
      {#each crumbs as crumb, index (index)}
        {@const last = index === crumbs.length - 1}
        <!-- Phones show where you are; the levels above it are Back and the sidebar. -->
        <li class={['flex items-center gap-1.5', last ? 'min-w-0' : 'shrink-0 max-md:hidden']}>
          {#if index > 0}<span aria-hidden="true" class="max-md:hidden">/</span>{/if}
          {#if crumb.href && !last}
            <a href={crumb.href} class="text-muted-foreground no-underline hover:underline">{crumb.label}</a>
          {:else}
            <span aria-current={last ? 'page' : undefined} class="min-w-0">
              <TruncatedText text={crumb.label} class="font-bold text-foreground" />
            </span>
          {/if}
        </li>
      {/each}
    </ol>
  </nav>

  <Tooltip.Root>
    <Tooltip.Trigger>
      {#snippet child({ props })}
        <button
          {...props}
          type="button"
          onclick={() => shell.openPalette()}
          aria-keyshortcuts={isMac ? 'Meta+K' : 'Control+K'}
          class="flex size-9 shrink-0 items-center justify-center gap-2 rounded-lg border border-border bg-muted text-muted-foreground hover:border-input pointer-coarse:size-11 md:w-full md:max-w-75 md:flex-[0_1_300px] md:justify-start md:px-3 md:pointer-coarse:h-11"
        >
          <SearchIcon aria-hidden="true" />
          <span class="max-md:sr-only md:flex-1 md:truncate md:text-left">Search</span>
          <KbdGroup class="max-md:hidden">{#each paletteKeys as key (key)}<Kbd>{key}</Kbd>{/each}</KbdGroup>
        </button>
      {/snippet}
    </Tooltip.Trigger>
    <!-- The bar shows the key caps from md up; below it the button is an icon and its tooltip names it (A4). -->
    <Tooltip.Content class="md:hidden">Search</Tooltip.Content>
  </Tooltip.Root>

  <Tooltip.Root>
    <Tooltip.Trigger>
      {#snippet child({ props })}
        <Button
          {...props}
          variant="outline"
          size="icon-lg"
          onclick={() => shell.openShortcuts()}
          aria-label="Keyboard shortcuts"
          class="max-md:hidden"
        >
          <KeyboardIcon aria-hidden="true" />
        </Button>
      {/snippet}
    </Tooltip.Trigger>
    <Tooltip.Content>
      Keyboard shortcuts
      {#if preferences.shortcutsEnabled}<Kbd>?</Kbd>{/if}
    </Tooltip.Content>
  </Tooltip.Root>

  <Tooltip.Root>
    <Tooltip.Trigger>
      {#snippet child({ props })}
        <Button
          {...props}
          variant="outline"
          size="icon-lg"
          onclick={() => preferences.toggleTheme()}
          aria-label={preferences.theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
        >
          {#if preferences.theme === 'dark'}<SunIcon aria-hidden="true" />{:else}<MoonIcon aria-hidden="true" />{/if}
        </Button>
      {/snippet}
    </Tooltip.Trigger>
    <Tooltip.Content>{preferences.theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}</Tooltip.Content>
  </Tooltip.Root>
</header>
