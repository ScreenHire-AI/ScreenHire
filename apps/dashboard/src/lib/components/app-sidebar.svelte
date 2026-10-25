<script lang="ts">
  import type { CurrentUser } from '@screenhire/api-client';
  import type { ConnectionStatus } from '@screenhire/realtime';
  import ChevronsUpDownIcon from '@lucide/svelte/icons/chevrons-up-down';
  import SettingsIcon from '@lucide/svelte/icons/settings';
  import type { Attachment } from 'svelte/attachments';
  import { SvelteSet } from 'svelte/reactivity';
  import { page } from '$app/state';
  import Hint from '#lib/components/hint.svelte';
  import LiveIndicator, { liveStatus } from '#lib/components/live-indicator.svelte';
  import LogoMark from '#lib/components/logo-mark.svelte';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Kbd } from '#lib/components/ui/kbd/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { initials } from '#lib/format.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { savedViews } from '#lib/saved-views.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import { isAdministrator, settingsSections } from '#lib/settings.js';
  import { getShell, sections } from '#lib/shell.js';

  // The app's navigation (design system sections 8 and 9): the full sidebar, the icon rail or the phone sheet, as the
  // provider around it decides. In the rail, labels hide and show in tooltips.
  let {
    user,
    viewCounts,
    live,
  }: {
    user: CurrentUser | null;
    /** Applications in each saved view, by view id; missing while the list loads. */
    viewCounts: Readonly<Record<string, number | undefined>>;
    live: ConnectionStatus;
  } = $props();

  const sidebar = Sidebar.useSidebar();
  const shell = getShell();
  const rail = $derived(sidebar.state === 'collapsed' && !sidebar.isMobile);
  const currentView = $derived(page.url.pathname === '/candidates' ? page.url.searchParams.get('view') : null);

  // A link in the phone sheet closes it, so the page it opens is not hidden behind the sheet.
  const closeSheet = () => sidebar.setOpenMobile(false);
  // Widening past phone size unmounts the sheet; it stays shut when the window narrows again.
  $effect(() => {
    if (!sidebar.isMobile && sidebar.openMobile) closeSheet();
  });

  // A menu button's tooltip is its label: shown in the rail, and wherever the label is cut with an ellipsis (F5).
  const cut = new SvelteSet<string>();
  function truncation(key: string): Attachment<HTMLElement> {
    return (element) => {
      const measure = () => (element.scrollWidth > element.clientWidth ? cut.add(key) : cut.delete(key));
      measure();
      const observer = new ResizeObserver(measure);
      observer.observe(element);
      return () => {
        observer.disconnect();
        cut.delete(key);
      };
    };
  }
  const tooltipFor = (key: string) => ({ hidden: !rail && !cut.has(key) });
</script>

<Sidebar.Root collapsible="icon">
  <Sidebar.Header>
    <a href="/" onclick={closeSheet} class="flex h-10 items-center gap-2.5 px-1.5 pointer-coarse:h-11 text-foreground no-underline group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:px-0">
      <LogoMark />
      <span class="font-display text-heading font-bold group-data-[collapsible=icon]:sr-only">ScreenHire</span>
    </a>

    {#if user?.organization}
      {@const organization = user.organization}
      <!-- Workspace switcher: every workspace the person belongs to. Today that is one organization per account. -->
      <Sidebar.Menu>
        <Sidebar.MenuItem>
          <DropdownMenu.Root>
            <DropdownMenu.Trigger>
              {#snippet child({ props })}
                <Sidebar.MenuButton
                  {...props}
                  size="lg"
                  tooltipContent={organization.name}
                  tooltipContentProps={tooltipFor('workspace')}
                  class="border border-border bg-card"
                >
                  <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-md bg-secondary text-caption font-semibold text-secondary-foreground"
                    >{initials(organization.name)}</span
                  >
                  <span
                    {@attach truncation('workspace')}
                    class="min-w-0 flex-1 truncate font-semibold text-foreground group-data-[collapsible=icon]:sr-only"
                    ><span class="sr-only">Workspace: </span>{organization.name}</span
                  >
                  <ChevronsUpDownIcon class="text-muted-foreground group-data-[collapsible=icon]:hidden" aria-hidden="true" />
                </Sidebar.MenuButton>
              {/snippet}
            </DropdownMenu.Trigger>
            <DropdownMenu.Content side={rail ? 'right' : 'bottom'} align="start" class="w-60">
              <DropdownMenu.Label class="text-caption text-muted-foreground">Workspaces</DropdownMenu.Label>
              <DropdownMenu.RadioGroup value={organization.id}>
                <DropdownMenu.RadioItem value={organization.id}>
                  <span class="flex flex-col">
                    <span class="font-semibold">{organization.name}</span>
                    <span class="text-caption text-muted-foreground">{organization.role}</span>
                  </span>
                </DropdownMenu.RadioItem>
              </DropdownMenu.RadioGroup>
            </DropdownMenu.Content>
          </DropdownMenu.Root>
        </Sidebar.MenuItem>
      </Sidebar.Menu>
    {/if}
  </Sidebar.Header>

  <Sidebar.Content>
    <nav aria-label="Main" class="flex min-h-0 flex-col">
      <Sidebar.Group>
        <Sidebar.Menu>
          {#each sections as section (section.href)}
            {@const Icon = section.icon}
            <Sidebar.MenuItem>
              <Sidebar.MenuButton
                isActive={section.matches(page.url.pathname)}
                aria-keyshortcuts={preferences.shortcutsEnabled ? `G ${section.key.toUpperCase()}` : undefined}
                onclick={closeSheet}
              >
                {#snippet tooltipContent()}
                  {section.label}
                  {#if preferences.shortcutsEnabled}<Kbd>G</Kbd><Kbd>{section.key.toUpperCase()}</Kbd>{/if}
                {/snippet}
                {#snippet child({ props })}
                  <a href={section.href} {...props}>
                    <Icon aria-hidden="true" />
                    <span class="group-data-[collapsible=icon]:sr-only">{section.label}</span>
                  </a>
                {/snippet}
              </Sidebar.MenuButton>
            </Sidebar.MenuItem>
          {/each}
        </Sidebar.Menu>
      </Sidebar.Group>

      <Sidebar.Group>
        <Sidebar.GroupLabel>
          {#snippet child({ props })}<h2 {...props}>Saved views</h2>{/snippet}
        </Sidebar.GroupLabel>
        <Sidebar.Menu>
          {#each savedViews as view (view.id)}
            {@const count = viewCounts[view.id]}
            <Sidebar.MenuItem>
              <Sidebar.MenuButton
                isActive={currentView === view.id}
                tooltipContent={count === undefined ? view.label : `${view.label}, ${count}`}
                tooltipContentProps={tooltipFor(view.id)}
                onclick={closeSheet}
              >
                {#snippet child({ props })}
                  <a href={`/candidates?view=${view.id}`} {...props}>
                    {#if view.status}
                      <!-- A status view shows that status's icon, in its colour (section 22); other views an ordinary icon. -->
                      <StatusIcon status={view.status} class="size-4 group-data-[collapsible=icon]:size-5" />
                    {:else}
                      {@const Icon = view.icon}
                      <Icon aria-hidden="true" />
                    {/if}
                    <span {@attach truncation(view.id)} class="min-w-0 truncate group-data-[collapsible=icon]:sr-only">{view.label}</span>
                    {#if count !== undefined}
                      <Badge variant="count" class="group-data-[collapsible=icon]:hidden">{count}</Badge>
                    {/if}
                  </a>
                {/snippet}
              </Sidebar.MenuButton>
            </Sidebar.MenuItem>
          {/each}
        </Sidebar.Menu>
      </Sidebar.Group>

      {#if isAdministrator(user?.organization?.role)}
        <!-- One item for owners and admins only; its pages share the Settings menu (section 11), and say so to anyone else
             who opens one by its address. -->
        <Sidebar.Group>
          <Sidebar.Menu>
            <Sidebar.MenuItem>
              <Sidebar.MenuButton
                isActive={page.url.pathname.startsWith('/settings/')}
                tooltipContent="Settings"
                tooltipContentProps={tooltipFor('settings')}
                onclick={closeSheet}
              >
                {#snippet child({ props })}
                  <a href={settingsSections[0]!.href} {...props}>
                    <SettingsIcon aria-hidden="true" />
                    <span {@attach truncation('settings')} class="min-w-0 truncate group-data-[collapsible=icon]:sr-only">Settings</span>
                  </a>
                {/snippet}
              </Sidebar.MenuButton>
            </Sidebar.MenuItem>
          </Sidebar.Menu>
        </Sidebar.Group>
      {/if}
    </nav>
  </Sidebar.Content>

  <Sidebar.Footer>
    <!-- In the rail the status keeps its label in a tooltip: shape, colour and label, always (T1). -->
    <Hint content={liveStatus[live].label} disabled={!rail}>
      {#snippet children({ props })}
        <div {...props} class="rounded-md">
          <LiveIndicator status={live} />
        </div>
      {/snippet}
    </Hint>

    {#if user}
      <Sidebar.Menu>
        <Sidebar.MenuItem>
          <DropdownMenu.Root>
            <DropdownMenu.Trigger>
              {#snippet child({ props })}
                <Sidebar.MenuButton
                  {...props}
                  size="lg"
                  tooltipContent={user.organization ? `${user.displayName}, ${user.organization.name}` : user.displayName}
                  tooltipContentProps={{ hidden: !rail && !cut.has('user') && !cut.has('user-organization') }}
                >
                  <span
                    aria-hidden="true"
                    class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-semibold text-secondary-foreground"
                    >{initials(user.displayName)}</span
                  >
                  <span class="flex min-w-0 flex-1 flex-col group-data-[collapsible=icon]:sr-only">
                    <span {@attach truncation('user')} class="truncate font-semibold text-sidebar-foreground">{user.displayName}</span>
                    <span {@attach truncation('user-organization')} class="truncate text-caption text-muted-foreground">{user.organization?.name}</span>
                  </span>
                </Sidebar.MenuButton>
              {/snippet}
            </DropdownMenu.Trigger>
            <DropdownMenu.Content side={rail ? 'right' : 'top'} align="start" class="w-60">
              <DropdownMenu.Label class="flex flex-col gap-0.5">
                <span class="text-caption text-muted-foreground">Signed in as</span>
                <span class="font-normal break-all">{user.email}</span>
              </DropdownMenu.Label>
              <DropdownMenu.Separator />
              <!-- The top bar's keyboard button is not on phones; the sheet holds this menu there. The dialog opens over the
                   sheet, so closing it brings the person back to where they were. -->
              <DropdownMenu.Item onSelect={() => shell.openShortcuts()}>
                Keyboard shortcuts
                {#if preferences.shortcutsEnabled}<DropdownMenu.Shortcut><Kbd>?</Kbd></DropdownMenu.Shortcut>{/if}
              </DropdownMenu.Item>
              <DropdownMenu.CheckboxItem
                checked={preferences.shortcutsEnabled}
                onCheckedChange={(checked) => preferences.setShortcutsEnabled(checked)}>Single-key shortcuts</DropdownMenu.CheckboxItem
              >
              <DropdownMenu.Separator />
              <DropdownMenu.Item onSelect={signOutAndRedirect}>Sign out</DropdownMenu.Item>
            </DropdownMenu.Content>
          </DropdownMenu.Root>
        </Sidebar.MenuItem>
      </Sidebar.Menu>
    {/if}
  </Sidebar.Footer>
</Sidebar.Root>
