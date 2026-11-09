<script lang="ts">
  import type { CalendarStatus, CurrentUser, SavedViewSummary, SlackStatus } from '@screenhire/api-client';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import ChevronsUpDownIcon from '~icons/lucide/chevrons-up-down';
  import EllipsisIcon from '~icons/lucide/ellipsis';
  import EyeIcon from '~icons/lucide/eye';
  import EyeOffIcon from '~icons/lucide/eye-off';
  import LockIcon from '~icons/lucide/lock';
  import PencilIcon from '~icons/lucide/pencil';
  import Trash2Icon from '~icons/lucide/trash-2';
  import UsersIcon from '~icons/lucide/users';
  import type { Attachment } from 'svelte/attachments';
  import { SvelteSet } from 'svelte/reactivity';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import FailureText from '#lib/components/failure-text.svelte';
  import LogoMark from '#lib/components/logo-mark.svelte';
  import CalendarAccountSheet from '#lib/components/calendar-account-sheet.svelte';
  import RenameViewDialog from '#lib/components/rename-view-dialog.svelte';
  import SlackAccountSheet from '#lib/components/slack-account-sheet.svelte';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Kbd } from '#lib/components/ui/kbd/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { initials } from '#lib/format.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import type { SidebarView } from '#lib/sidebar-views.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import { settingsAreaOf, visibleSettingsAreas } from '#lib/settings.js';
  import { calendarFailure, isCalendarOutcome } from '#lib/calendar.js';
  import { slackFailure } from '#lib/slack.js';
  import { getShell, sections } from '#lib/shell.js';
  import type { ViewActions } from '#lib/views.svelte.js';

  // The app's navigation (design system sections 8 and 9): the full sidebar, the icon rail or the phone sheet, as the
  // provider around it decides. In the rail, labels hide and show in tooltips.
  let {
    user,
    views,
    viewActions,
    slack,
    calendar,
    calendarOpen = $bindable(false),
  }: {
    user: CurrentUser | null;
    /**
     * The Views list: the built-in views the person has not hidden, then the saved ones they see, each with its count
     * (missing while the list loads), and the built-in ones they hid.
     */
    views: { visible: readonly SidebarView[]; hidden: readonly SidebarView[] };
    /** What the views menus do; without it the rows are plain links. */
    viewActions?: ViewActions;
    /** Slack for this person (`GET /v1/slack`), for the Slack account item in their menu; missing while it loads. */
    slack?: SlackStatus;
    /** The person's calendar (`GET /v1/calendar`), for the Calendar item in their menu; missing while it loads. */
    calendar?: CalendarStatus;
    /** The Calendar sheet is open. The shell opens it for a page that needs the person's calendar connected. */
    calendarOpen?: boolean;
  } = $props();

  const sidebar = Sidebar.useSidebar();
  const shell = getShell();
  const rail = $derived(sidebar.state === 'collapsed' && !sidebar.isMobile);
  // A view opens on the Candidates page, or on a job's candidates tab; its id is in the address either way.
  const currentView = $derived(
    page.url.pathname === '/candidates' || page.url.pathname.startsWith('/jobs/') ? page.url.searchParams.get('view') : null,
  );
  let showHidden = $state(false);
  let renaming = $state<SavedViewSummary | null>(null);
  // An item that opens a dialog queues it, and the dialog opens once the menu has closed and given focus back.
  let renameQueued: SavedViewSummary | null = null;
  let viewFailure = $state('');
  async function run(work: Promise<string | null>) {
    viewFailure = (await work) ?? '';
  }
  const settingsAreaItems = $derived(visibleSettingsAreas(user, shell.features()));

  // Each person's own Slack account, in their menu while the organization's workspace is connected (docs/architecture/slack.md).
  const slackConnected = $derived(Boolean(slack?.available && slack.workspace));
  let slackOpen = $state(false);
  let slackOutcome = $state('');
  // Linking comes back to the page it started on with ?slack-account=: the sheet opens to show how it went, and the
  // parameter leaves the address so a reload does not repeat it.
  $effect(() => {
    const outcome = page.url.searchParams.get('slack-account');
    if (outcome === null) return;
    slackOutcome = slackFailure(outcome) ?? '';
    slackOpen = true;
    const url = new URL(page.url.href);
    url.searchParams.delete('slack-account');
    void goto(url, { replace: true, reset: false });
  });

  // Each person's own calendar, in their menu once this ScreenHire has a calendar to connect (docs/architecture/interview-scheduling.md).
  const calendarAvailable = $derived(Boolean(calendar && calendar.providers.length > 0));
  let calendarOutcome = $state('');
  // An outcome belongs to the visit it came back with, so the sheet opens clean when a page opens it through the shell.
  $effect(() => {
    if (!calendarOpen) calendarOutcome = '';
  });
  // Connecting comes back to the page it started on with ?calendar=: the sheet opens to show how it went, and the
  // parameter leaves the address so a reload does not repeat it.
  $effect(() => {
    const outcome = page.url.searchParams.get('calendar');
    if (!isCalendarOutcome(outcome)) return;
    calendarOutcome = calendarFailure(outcome) ?? '';
    calendarOpen = true;
    const url = new URL(page.url.href);
    url.searchParams.delete('calendar');
    void goto(url, { replace: true, reset: false });
  });

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
                    <span class="text-caption text-muted-foreground">{organization.roleName}</span>
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
    <nav aria-label="Main" class="flex shrink-0 grow flex-col">
      <Sidebar.Group>
        <Sidebar.Menu>
          {#each sections.filter((section) => section.sidebar !== false) as section (section.href)}
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
          {#snippet child({ props })}<h2 {...props}>Views</h2>{/snippet}
        </Sidebar.GroupLabel>
        <Sidebar.Menu>
          {#each views.visible as view (view.key)}
            {@render viewRow(view, false)}
          {/each}
          {#if views.hidden.length > 0}
            <Sidebar.MenuItem class="group-data-[collapsible=icon]:hidden">
              <Sidebar.MenuButton aria-expanded={showHidden} onclick={() => (showHidden = !showHidden)} class="text-muted-foreground">
                <span class="min-w-0 flex-1 truncate">Show hidden views</span>
                <Badge variant="count">{views.hidden.length}</Badge>
                <ChevronDownIcon aria-hidden="true" class={['transition-transform motion-reduce:transition-none', showHidden && 'rotate-180']} />
              </Sidebar.MenuButton>
            </Sidebar.MenuItem>
            {#if showHidden && !rail}
              {#each views.hidden as view (view.key)}
                {@render viewRow(view, true)}
              {/each}
            {/if}
          {/if}
        </Sidebar.Menu>
        <FailureText message={viewFailure} class="px-2 group-data-[collapsible=icon]:hidden" />
      </Sidebar.Group>

      {#if settingsAreaItems.length > 0}
        <!-- Up to three items at the bottom: Organization, Hiring setup and Integrations. Each shows for anyone whose role
             has a permission for a page in it, and opens the first page they may use. A group's pages share the Settings
             menu (section 11), and say so to anyone else who opens one by its address. -->
        <Sidebar.Group class="sticky bottom-0 mt-auto bg-sidebar">
          <Sidebar.Menu>
            {#each settingsAreaItems as area (area.group)}
              {@const Icon = area.icon}
              <Sidebar.MenuItem>
                <Sidebar.MenuButton
                  isActive={settingsAreaOf(page.url.pathname) === area.group}
                  tooltipContent={area.label}
                  tooltipContentProps={tooltipFor(area.group)}
                  onclick={closeSheet}
                >
                  {#snippet child({ props })}
                    <a href={area.href} {...props}>
                      <Icon aria-hidden="true" />
                      <span {@attach truncation(area.group)} class="min-w-0 truncate group-data-[collapsible=icon]:sr-only">{area.label}</span>
                    </a>
                  {/snippet}
                </Sidebar.MenuButton>
              </Sidebar.MenuItem>
            {/each}
          </Sidebar.Menu>
        </Sidebar.Group>
      {/if}
    </nav>
  </Sidebar.Content>

  {#if user}
    <!-- A hairline in the sidebar border colour sets the person's block apart from the navigation above it: where to go,
         then who you are (section 11). It sits outside the scrolling content, so it holds in the rail and the sheet too.
         `h-px` is explicit: the Separator's own height waits on a `data-horizontal` attribute bits-ui does not set. -->
    <Sidebar.Separator decorative class="h-px" />
  {/if}
  <Sidebar.Footer>
    {#if user}
      <Sidebar.Menu>
        <Sidebar.MenuItem>
          <DropdownMenu.Root>
            <DropdownMenu.Trigger>
              {#snippet child({ props })}
                <Sidebar.MenuButton
                  {...props}
                  size="lg"
                  tooltipContent={user.organization ? `${user.displayName}, ${user.organization.roleName}` : user.displayName}
                  tooltipContentProps={{ hidden: !rail && !cut.has('user') && !cut.has('user-organization') }}
                >
                  <span
                    aria-hidden="true"
                    class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-semibold text-secondary-foreground"
                    >{initials(user.displayName)}</span
                  >
                  <span class="flex min-w-0 flex-1 flex-col group-data-[collapsible=icon]:sr-only">
                    <span {@attach truncation('user')} class="truncate font-semibold text-sidebar-foreground">{user.displayName}</span>
                    <!-- The role, not the organization: the workspace above already names it (W7). -->
                    <span {@attach truncation('user-organization')} class="truncate text-caption text-muted-foreground">{user.organization?.roleName}</span>
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
              <DropdownMenu.Item onSelect={() => shell.openNotifications()}>Notifications</DropdownMenu.Item>
              <DropdownMenu.CheckboxItem
                checked={preferences.shortcutsEnabled}
                onCheckedChange={(checked) => preferences.setShortcutsEnabled(checked)}>Single-key shortcuts</DropdownMenu.CheckboxItem
              >
              {#if calendarAvailable}
                <DropdownMenu.Separator />
                <DropdownMenu.Item onSelect={() => ((calendarOutcome = ''), (calendarOpen = true))}>Calendar</DropdownMenu.Item>
              {/if}
              {#if slackConnected}
                <DropdownMenu.Separator />
                <DropdownMenu.Item onSelect={() => ((slackOutcome = ''), (slackOpen = true))}>Slack account</DropdownMenu.Item>
              {/if}
              <DropdownMenu.Separator />
              <DropdownMenu.Item onSelect={signOutAndRedirect}>Sign out</DropdownMenu.Item>
            </DropdownMenu.Content>
          </DropdownMenu.Root>
        </Sidebar.MenuItem>
      </Sidebar.Menu>
    {/if}
  </Sidebar.Footer>
</Sidebar.Root>

{#if calendar && calendarAvailable}
  <CalendarAccountSheet bind:open={calendarOpen} status={calendar} outcome={calendarOutcome} />
{/if}
{#if slack?.available}
  <SlackAccountSheet bind:open={slackOpen} status={slack} outcome={slackOutcome} />
{/if}

<!-- One row of the Views list: the link with its icon, name and count, and for a person who may change it the menu that shows
     on hover and on focus (A4: the button has a label and a tooltip). A built-in view's menu hides it; a saved view's
     renames, shares or deletes it. A saved view the person cannot change has no menu. -->
{#snippet viewRow(view: SidebarView, hiddenRow: boolean)}
  {@const count = view.count}
  {@const saved = view.saved}
  {@const actions = viewActions}
  {@const menu = actions && (view.builtIn || saved?.canEdit)}
  <Sidebar.MenuItem>
    <Sidebar.MenuButton
      isActive={currentView === view.key}
      tooltipContent={[view.label, view.jobTitle, count === undefined ? undefined : String(count)].filter(Boolean).join(', ')}
      tooltipContentProps={view.jobTitle ? { hidden: false } : tooltipFor(view.key)}
      onclick={closeSheet}
      class={menu ? 'pr-8 pointer-coarse:pr-12' : undefined}
    >
      {#snippet child({ props })}
        <a href={view.href} {...props}>
          {#if view.status}
            <!-- A status view shows that status's icon, in its colour (section 21); other views an ordinary icon. -->
            <StatusIcon status={view.status} class="size-4 group-data-[collapsible=icon]:size-5" />
          {:else if view.icon}
            {@const Icon = view.icon}
            <Icon aria-hidden="true" />
          {/if}
          <span {@attach truncation(view.key)} class="min-w-0 flex-1 truncate group-data-[collapsible=icon]:sr-only">{view.label}{#if saved?.shared}<span class="sr-only">, shared with the team</span>{/if}</span>
          {#if count !== undefined}
            <Badge variant="count" class="group-data-[collapsible=icon]:hidden">{count}</Badge>
          {/if}
        </a>
      {/snippet}
    </Sidebar.MenuButton>
    {#if menu && actions}
      <DropdownMenu.Root>
        <Tooltip.Root>
          <Tooltip.Trigger>
            {#snippet child({ props: tip })}
              <DropdownMenu.Trigger {...tip} aria-label={`Actions for ${view.label}`}>
                {#snippet child({ props })}
                  <Sidebar.MenuAction {...props} showOnHover class="w-6 peer-data-[size=default]/menu-button:top-1 pointer-coarse:w-11 pointer-coarse:opacity-100!"><EllipsisIcon aria-hidden="true" /></Sidebar.MenuAction>
                {/snippet}
              </DropdownMenu.Trigger>
            {/snippet}
          </Tooltip.Trigger>
          <Tooltip.Content side="right">Actions for {view.label}</Tooltip.Content>
        </Tooltip.Root>
        <DropdownMenu.Content
          side={sidebar.isMobile ? 'bottom' : 'right'}
          align="start"
          class="min-w-52"
          onCloseAutoFocus={() => {
            const queued = renameQueued;
            renameQueued = null;
            if (queued) setTimeout(() => (renaming = queued));
          }}
        >
          {#if view.builtIn}
            <DropdownMenu.Item onSelect={() => void run(actions.setHidden(view.key, !hiddenRow))}>
              {#if hiddenRow}<EyeIcon aria-hidden="true" />Show in sidebar{:else}<EyeOffIcon aria-hidden="true" />Hide from sidebar{/if}
            </DropdownMenu.Item>
          {:else if saved}
            <DropdownMenu.Item onSelect={() => (renameQueued = saved)}><PencilIcon aria-hidden="true" />Rename</DropdownMenu.Item>
            <DropdownMenu.Item onSelect={() => void run(actions.setShared(saved, !saved.shared))}>
              {#if saved.shared}<LockIcon aria-hidden="true" />Make private{:else}<UsersIcon aria-hidden="true" />Share with the team{/if}
            </DropdownMenu.Item>
            <DropdownMenu.Separator />
            <DropdownMenu.Item variant="destructive" onSelect={() => void run(actions.remove(saved))}>
              <Trash2Icon aria-hidden="true" />Delete view
            </DropdownMenu.Item>
          {/if}
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    {/if}
  </Sidebar.MenuItem>
{/snippet}

{#if viewActions && renaming}
  {@const actions = viewActions}
  <RenameViewDialog view={renaming} onrename={(view, name) => actions.rename(view, name)} onclose={() => (renaming = null)} />
{/if}
