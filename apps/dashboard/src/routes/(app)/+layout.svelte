<script lang="ts">
  import { createListApplications } from '@screenhire/api-client/svelte-query';
  import { setQueryClientContext } from '@tanstack/svelte-query';
  import ChevronsUpDownIcon from '@lucide/svelte/icons/chevrons-up-down';
  import { onDestroy } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { clock } from '#lib/clock.svelte.js';
  import CommandPalette from '#lib/components/command-palette.svelte';
  import LiveIndicator from '#lib/components/live-indicator.svelte';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import StatusMark from '#lib/components/status-mark.svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { initials } from '#lib/format.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { createQueryClient } from '#lib/query.js';
  import { startLiveUpdates } from '#lib/realtime.js';
  import { savedViews } from '#lib/saved-views.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import { sections, setShell } from '#lib/shell.js';
  import { createChordHandler } from '#lib/shortcuts.js';
  import type { LayoutProps } from './$types';

  let { data, children }: LayoutProps = $props();

  // One query cache and one hub connection for the signed-in shell. Leaving it (signing out) drops both, so nothing
  // cached for this account outlives the session.
  const queryClient = createQueryClient();
  setQueryClientContext(queryClient);
  const live = startLiveUpdates(queryClient);
  onDestroy(() => {
    void live.stop();
    queryClient.clear();
  });

  let paletteOpen = $state(false);
  let shortcutsOpen = $state(false);
  setShell({ openPalette: () => (paletteOpen = true), openShortcuts: () => (shortcutsOpen = true) });

  const chords = createChordHandler(Object.fromEntries(sections.map((section) => [section.key, section.href])), (href) => void goto(href));

  function onkeydown(event: KeyboardEvent) {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      paletteOpen = !paletteOpen;
      return;
    }

    if (preferences.shortcutsEnabled && !event.defaultPrevented && chords(event)) {
      event.preventDefault();
    }
  }

  // Saved-view counts come from the same list the candidates page shows, so they agree and update live.
  const applications = createListApplications();
  const viewCounts = $derived(
    Object.fromEntries(
      savedViews.map((view) => [view.id, applications.data?.data.filter((application) => view.matches(application, clock.now)).length]),
    ),
  );

  const user = $derived(data.user);
  const currentView = $derived(page.url.pathname === '/candidates' ? page.url.searchParams.get('view') : null);
</script>

<!-- Capture, so a G chord sees its second key before a table's J / K handler can claim it. -->
<svelte:window onkeydowncapture={onkeydown} />

<div class="flex min-h-screen flex-wrap">
  <nav
    aria-label="Main"
    class="flex max-w-full min-w-0 flex-[1_1_232px] flex-col gap-3.5 border-r border-sidebar-border bg-sidebar px-3 py-3.5"
  >
    <a href="/" class="flex items-center gap-2.5 px-1.5 py-1 text-foreground no-underline">
      <span class="grid size-7 place-items-center rounded-lg bg-foreground font-display font-extrabold text-background">S</span>
      <span class="font-display text-heading font-bold">ScreenHire</span>
    </a>

    {#if user?.organization}
      <!-- Workspace switcher: every workspace the person belongs to. Today that is one organization per account. -->
      <DropdownMenu.Root>
        <DropdownMenu.Trigger
          aria-label={`Workspace: ${user.organization.name}`}
          class="flex h-[34px] items-center justify-between rounded-lg border border-border bg-card px-2.5 text-left font-semibold text-foreground hover:bg-secondary"
        >
          <span class="truncate">{user.organization.name}</span>
          <ChevronsUpDownIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        </DropdownMenu.Trigger>
        <DropdownMenu.Content align="start" class="w-60">
          <DropdownMenu.Label class="text-caption text-muted-foreground">Workspaces</DropdownMenu.Label>
          <DropdownMenu.RadioGroup value={user.organization.id}>
            <DropdownMenu.RadioItem value={user.organization.id}>
              <span class="flex flex-col">
                <span class="font-semibold">{user.organization.name}</span>
                <span class="text-caption text-muted-foreground">{user.organization.role}</span>
              </span>
            </DropdownMenu.RadioItem>
          </DropdownMenu.RadioGroup>
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    {/if}

    <ul class="flex flex-col gap-0.5">
      {#each sections as section (section.href)}
        {@const current = section.matches(page.url.pathname)}
        <li>
          <a
            href={section.href}
            aria-current={current ? 'page' : undefined}
            aria-keyshortcuts={preferences.shortcutsEnabled ? `G ${section.key.toUpperCase()}` : undefined}
            class={[
              'flex h-8 items-center justify-between rounded-md px-2.5 no-underline',
              current
                ? 'bg-sidebar-accent font-bold text-sidebar-accent-foreground'
                : 'font-semibold text-sidebar-foreground hover:bg-secondary',
            ]}
          >
            {section.label}
            {#if preferences.shortcutsEnabled}
              <kbd class="font-mono text-code font-normal text-muted-foreground">G {section.key.toUpperCase()}</kbd>
            {/if}
          </a>
        </li>
      {/each}
    </ul>

    <section aria-labelledby="saved-views-heading" class="flex flex-col gap-0.5">
      <h2 id="saved-views-heading" class="px-2.5 text-caption text-muted-foreground">
        Saved views
      </h2>
      <ul class="flex flex-col gap-0.5">
        {#each savedViews as view (view.id)}
          <li>
            <a
              href={`/candidates?view=${view.id}`}
              aria-current={currentView === view.id ? 'page' : undefined}
              class={[
                'flex h-8 items-center justify-between gap-2 rounded-md px-2.5 text-sidebar-foreground no-underline hover:bg-secondary',
                currentView === view.id && 'bg-sidebar-accent font-bold text-sidebar-accent-foreground',
              ]}
            >
              <span class="flex min-w-0 items-center gap-2">
                <StatusMark shape={view.shape} tone={view.tone} />
                <span class="truncate">{view.label}</span>
              </span>
              <span class="text-caption tabular-nums">{viewCounts[view.id] ?? ''}</span>
            </a>
          </li>
        {/each}
      </ul>
    </section>

    <div class="mt-auto flex flex-col gap-2">
      <LiveIndicator status={live.connection.status} />
      {#if user}
        <DropdownMenu.Root>
          <DropdownMenu.Trigger
            class="flex w-full items-center gap-2.5 rounded-md px-1.5 py-1.5 text-left hover:bg-secondary"
          >
            <span
              aria-hidden="true"
              class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-semibold text-secondary-foreground"
              >{initials(user.displayName ?? '')}</span
            >
            <span class="flex min-w-0 flex-col">
              <span class="truncate font-semibold text-sidebar-foreground">{user.displayName}</span>
              <span class="truncate text-caption text-muted-foreground">{user.organization?.name}</span>
            </span>
          </DropdownMenu.Trigger>
          <DropdownMenu.Content align="start" class="w-60">
            <DropdownMenu.Label class="flex flex-col gap-0.5">
              <span class="text-caption text-muted-foreground">Signed in as</span>
              <span class="truncate font-normal">{user.email}</span>
            </DropdownMenu.Label>
            <DropdownMenu.Separator />
            <DropdownMenu.CheckboxItem
              checked={preferences.shortcutsEnabled}
              onCheckedChange={(checked) => preferences.setShortcutsEnabled(checked)}>Single-key shortcuts</DropdownMenu.CheckboxItem
            >
            <DropdownMenu.Separator />
            <DropdownMenu.Item onSelect={signOutAndRedirect}>Sign out</DropdownMenu.Item>
          </DropdownMenu.Content>
        </DropdownMenu.Root>
      {/if}
    </div>
  </nav>

  <div class="flex min-w-0 flex-[999_1_560px] flex-col">
    {@render children()}
  </div>
</div>

<CommandPalette bind:open={paletteOpen} />
<ShortcutsDialog bind:open={shortcutsOpen} />
