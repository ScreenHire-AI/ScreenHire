<script lang="ts">
  import type { CalendarStatus, CurrentUser, SavedViewSummary } from '@screenhire/api-client';
  import CircleMinusIcon from '~icons/lucide/circle-minus';
  import ChevronsUpDownIcon from '~icons/lucide/chevrons-up-down';
  import EllipsisIcon from '~icons/lucide/ellipsis';
  import EyeIcon from '~icons/lucide/eye';
  import EyeOffIcon from '~icons/lucide/eye-off';
  import LockIcon from '~icons/lucide/lock';
  import PencilIcon from '~icons/lucide/pencil';
  import SettingsIcon from '~icons/lucide/settings';
  import Trash2Icon from '~icons/lucide/trash-2';
  import UsersIcon from '~icons/lucide/users';
  import UserRoundSearchIcon from '~icons/lucide/user-round-search';
  import type { Attachment } from 'svelte/attachments';
  import { SvelteSet } from 'svelte/reactivity';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import FailureText from '#lib/components/failure-text.svelte';
  import LogoMark from '#lib/components/logo-mark.svelte';
  import CalendarAccountSheet from '#lib/components/calendar-account-sheet.svelte';
  import RenameViewDialog from '#lib/components/rename-view-dialog.svelte';
  import TeamViewsSheet from '#lib/components/team-views-sheet.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Kbd } from '#lib/components/ui/kbd/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { formatDayMonth } from '#lib/dates.js';
  import { initials } from '#lib/format.js';
  import { can } from '#lib/permissions.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import type { SidebarView } from '#lib/sidebar-views.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import { featureOn, settingsHome, visibleSettingsSections } from '#lib/settings.js';
  import { calendarFailure, isCalendarOutcome } from '#lib/calendar.js';
  import { getShell, sectionsFor } from '#lib/shell.js';
  import type { ViewActions } from '#lib/views.svelte.js';

  // The app's navigation (design system sections 8 and 9): the full sidebar, the icon rail or the phone sheet, as the
  // provider around it decides. In the rail, labels hide and show in tooltips.
  let {
    user,
    views,
    viewActions,
    teamViews,
    calendar,
    calendarOpen = $bindable(false),
    openTasks,
    trialEndsAt,
  }: {
    user: CurrentUser | null;
    /**
     * The Views list: the built-in views the person has not hidden, then the saved ones they see, and the built-in ones
     * they hid. A view has no count (design system I2, T4).
     */
    views: { visible: readonly SidebarView[]; hidden: readonly SidebarView[] };
    /** What the views menus do; without it the rows are plain links. */
    viewActions?: ViewActions;
    /**
     * What the sheet of team views lists: every view in the person's list, when they last opened the sheet, and the job
     * titles for its filter lines. Without it (or without `viewActions`) the sidebar has no Browse team views button.
     */
    teamViews?: { views: readonly SavedViewSummary[]; seenAt: string; jobTitles: Readonly<Record<string, string>> };
    /** The person's calendar (`GET /v1/calendar`), for the Calendar sheet a page opens through the shell; missing while it loads. */
    calendar?: CalendarStatus;
    /** The Calendar sheet is open. The shell opens it for a page that needs the person's calendar connected. */
    calendarOpen?: boolean;
    /** How many open tasks are the person's own, in the count badge after My tasks; missing while it loads, and nothing at none. */
    openTasks?: number;
    /**
     * When the free trial ends, for an Owner while it runs: one quiet line above the person's block that links to Billing.
     * Missing for everyone else, and once a plan is paid or billing is off (docs/architecture/billing.md).
     */
    trialEndsAt?: string | null;
  } = $props();

  let teamViewsOpen = $state(false);
  // Browse team views opens once the Views menu has closed, so the sheet takes focus from the menu, not the other way.
  let teamViewsQueued = false;

  const sidebar = Sidebar.useSidebar();
  const shell = getShell();
  const rail = $derived(sidebar.state === 'collapsed' && !sidebar.isMobile);
  // The sheet of team views opens from the end of the Views list, or from the fixed group above Settings in the rail.
  const canBrowseTeamViews = $derived(Boolean(teamViews && viewActions));
  const railTeamViews = $derived(canBrowseTeamViews && rail);
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
  // One Settings item opens the first settings page for anyone whose role may use a setting; its menu lists them. Verification and
  // the rest of `sections` show for those whose role has the section's permission.
  const settingsShown = $derived(visibleSettingsSections(user, shell.features()).length > 0);
  const navSections = $derived(sectionsFor(user).filter((section) => section.sidebar !== false));
  const settingsActive = $derived(page.url.pathname === settingsHome || page.url.pathname.startsWith(`${settingsHome}/`));

  // Billing in the person's menu, for an Owner where a payment provider is set up (A3).
  const billingShown = $derived(can(user, 'ManageBilling') && featureOn(shell.features(), 'billing'));

  // Each person's own calendar, once this ScreenHire has a calendar to connect (docs/architecture/interview-scheduling.md). Your
  // account > Connected accounts shows it; this sheet is for a page that needs it connected (scheduling an interview).
  const calendarAvailable = $derived(Boolean(calendar && calendar.providers.length > 0));
  let calendarOutcome = $state('');
  // An outcome belongs to the visit it came back with, so the sheet opens clean when a page opens it through the shell.
  $effect(() => {
    if (!calendarOpen) calendarOutcome = '';
  });
  // Connecting from the sheet comes back to the page it started on with ?calendar=: the sheet opens to show how it went, and
  // the parameter leaves the address so a reload does not repeat it. Connected accounts reads its own.
  $effect(() => {
    const outcome = page.url.searchParams.get('calendar');
    if (!isCalendarOutcome(outcome) || page.url.pathname.startsWith('/account/')) return;
    calendarOutcome = calendarFailure(outcome) ?? '';
    calendarOpen = true;
    const url = new URL(page.url.href);
    url.searchParams.delete('calendar');
    void goto(url, { replace: true, reset: false });
  });

  // A link in the phone sheet closes it, so the page it opens is not hidden behind the sheet.
  const closeSheet = () => sidebar.setOpenMobile(false);
  // A link in the person's menu keeps the menu's own click (which closes it and lifts its pointer lock) and closes the phone sheet too.
  function closeMenuAndSheet(props: Record<string, unknown>, event: MouseEvent) {
    (props.onclick as ((event: MouseEvent) => void) | undefined)?.(event);
    closeSheet();
  }
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

  <!-- A thin scrollbar: in the icon rail the full one, with its arrows, was wider than the icons it scrolled (the owner's
       walk-through, 2026-11-21). -->
  <Sidebar.Content class="[scrollbar-width:thin]">
    <nav aria-label="Main" class="flex shrink-0 grow flex-col">
      <Sidebar.Group>
        <Sidebar.Menu>
          {#each navSections as section (section.href)}
            {@const Icon = section.icon}
            {@const count = section.href === '/tasks' ? openTasks : undefined}
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
                    <span class="min-w-0 flex-1 truncate group-data-[collapsible=icon]:sr-only">{section.label}</span>
                    {#if count}
                      <Badge variant="count" class="group-data-[collapsible=icon]:hidden">{count}</Badge>
                    {/if}
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
        {#if !rail && (canBrowseTeamViews || views.hidden.length > 0)}
          <!-- What acts on the views, not a view: Browse team views and the hidden built-ins sit in the heading's menu, so
               the list holds only views (the owner's walk-through, 2026-11-21). The rail keeps Browse team views as the
               icon above Settings, below. -->
          <DropdownMenu.Root>
            <DropdownMenu.Trigger>
              {#snippet child({ props })}
                <Sidebar.GroupAction {...props} aria-label="Views options" class="pointer-coarse:size-11"><EllipsisIcon aria-hidden="true" /></Sidebar.GroupAction>
              {/snippet}
            </DropdownMenu.Trigger>
            <DropdownMenu.Content
              side={sidebar.isMobile ? 'bottom' : 'right'}
              align="start"
              class="min-w-52"
              onCloseAutoFocus={(event) => {
                if (!teamViewsQueued) return;
                event.preventDefault();
                teamViewsQueued = false;
                teamViewsOpen = true;
              }}
            >
              {#if canBrowseTeamViews}
                <DropdownMenu.Item onSelect={() => (teamViewsQueued = true)}><UserRoundSearchIcon aria-hidden="true" />Browse team views</DropdownMenu.Item>
              {/if}
              {#if views.hidden.length > 0}
                <DropdownMenu.CheckboxItem checked={showHidden} onCheckedChange={(checked) => (showHidden = checked)}>
                  Show hidden views ({views.hidden.length})
                </DropdownMenu.CheckboxItem>
              {/if}
            </DropdownMenu.Content>
          </DropdownMenu.Root>
        {/if}
        <Sidebar.Menu>
          {#each views.visible as view (view.key)}
            {@render viewRow(view, false)}
          {/each}
          {#if showHidden && !rail}
            {#each views.hidden as view (view.key)}
              {@render viewRow(view, true)}
            {/each}
          {/if}
        </Sidebar.Menu>
        <FailureText message={viewFailure} class="px-2 group-data-[collapsible=icon]:hidden" />
      </Sidebar.Group>

      {#if settingsShown || railTeamViews}
        <!-- Fixed at the bottom, outside the Views list that scrolls. In the rail Browse team views is the icon above Settings, so
             it is always in reach; in the full sidebar it ends the Views list instead. One Settings item opens the first settings
             page, whose menu lists every setting the person's role may use, with search (section 11). -->
        <Sidebar.Group class="sticky bottom-0 mt-auto bg-sidebar">
          <Sidebar.Menu>
            {#if railTeamViews}
              <Sidebar.MenuItem>
                <Sidebar.MenuButton tooltipContent="Browse team views" onclick={() => (teamViewsOpen = true)} aria-haspopup="dialog">
                  <UserRoundSearchIcon aria-hidden="true" />
                  <span class="group-data-[collapsible=icon]:sr-only">Browse team views</span>
                </Sidebar.MenuButton>
              </Sidebar.MenuItem>
            {/if}
            {#if settingsShown}
              <Sidebar.MenuItem>
                <Sidebar.MenuButton isActive={settingsActive} tooltipContent="Settings" onclick={closeSheet}>
                  {#snippet child({ props })}
                    <a href={settingsHome} {...props}>
                      <SettingsIcon aria-hidden="true" />
                      <span class="group-data-[collapsible=icon]:sr-only">Settings</span>
                    </a>
                  {/snippet}
                </Sidebar.MenuButton>
              </Sidebar.MenuItem>
            {/if}
          </Sidebar.Menu>
        </Sidebar.Group>
      {/if}
    </nav>
  </Sidebar.Content>

  {#if user}
    <!-- A hairline in the sidebar border colour sets the person's block apart from the navigation above it: where to go,
         then who you are (section 11). It sits outside the scrolling content, so it holds in the rail and the sheet too. -->
    <Sidebar.Separator decorative />
  {/if}
  <Sidebar.Footer>
    {#if trialEndsAt}
      <!-- Not in the rail: a line of words has no icon to shrink to, and Billing is one click from Settings. On touch the 44px
           target (F4) is padding that a negative margin takes back, so the line costs the sheet only its text's height: the
           views scroll under the sticky Settings group, and a taller footer parks a row half under it. -->
      <a
        href="/settings/billing"
        onclick={closeSheet}
        class="flex min-h-6 min-w-0 items-center px-2 text-caption text-muted-foreground underline underline-offset-2 pointer-coarse:-my-3.5 pointer-coarse:min-h-0 pointer-coarse:py-3.5 group-data-[collapsible=icon]:hidden"
      >
        <span class="truncate">Trial ends <time datetime={trialEndsAt}>{formatDayMonth(trialEndsAt)}</time></span>
      </a>
    {/if}
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
                  {#if user.photoUrl}
                    <img src={user.photoUrl} alt="" class="size-7 shrink-0 rounded-full object-cover" />
                  {:else}
                    <span
                      aria-hidden="true"
                      class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-semibold text-secondary-foreground"
                      >{initials(user.displayName)}</span
                    >
                  {/if}
                  <span class="flex min-w-0 flex-1 flex-col group-data-[collapsible=icon]:sr-only">
                    <span {@attach truncation('user')} class="truncate font-semibold text-sidebar-foreground">{user.displayName}</span>
                    <!-- The role, not the organization: the workspace above already names it (W7). -->
                    <span {@attach truncation('user-organization')} class="truncate text-caption text-muted-foreground">{user.organization?.roleName}</span>
                  </span>
                </Sidebar.MenuButton>
              {/snippet}
            </DropdownMenu.Trigger>
            <DropdownMenu.Content side={rail ? 'right' : 'top'} align="start" class="w-60">
              <!-- Who is signed in, then the person's own things: Your account holds the rest (design system section 21). -->
              <DropdownMenu.Label class="flex flex-col gap-0.5">
                <span class="break-all">{user.displayName}</span>
                <span class="font-normal break-all text-muted-foreground">{user.email}</span>
              </DropdownMenu.Label>
              <DropdownMenu.Separator />
              <DropdownMenu.Item>
                {#snippet child({ props })}<a {...props} href="/account" onclick={(event) => closeMenuAndSheet(props, event)}>Your account</a>{/snippet}
              </DropdownMenu.Item>
              <!-- The top bar's keyboard button is not on phones; the sheet holds this menu there. The dialog opens over the
                   sheet, so closing it brings the person back to where they were. -->
              <DropdownMenu.Item onSelect={() => shell.openShortcuts()}>
                Keyboard shortcuts
                {#if preferences.shortcutsEnabled}<DropdownMenu.Shortcut><Kbd>?</Kbd></DropdownMenu.Shortcut>{/if}
              </DropdownMenu.Item>
              {#if billingShown}
                <DropdownMenu.Item>
                  {#snippet child({ props })}<a {...props} href="/settings/billing" onclick={(event) => closeMenuAndSheet(props, event)}>Billing</a>{/snippet}
                </DropdownMenu.Item>
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

{#if teamViews && viewActions}
  <TeamViewsSheet bind:open={teamViewsOpen} views={teamViews.views} seenAt={teamViews.seenAt} jobTitles={teamViews.jobTitles} actions={viewActions} onnavigate={closeSheet} />
{/if}
{#if calendar && calendarAvailable}
  <CalendarAccountSheet bind:open={calendarOpen} status={calendar} outcome={calendarOutcome} />
{/if}

<!-- One row of the Views list: the link with its icon and name, and for a person who may change it the menu that shows
     on hover and on focus (A4: the button has a label and a tooltip). A built-in view's menu hides it; a saved view's
     renames, shares or deletes it when the person may change it, and a teammate's shared view they added can be removed
     from the sidebar. The person's own view that they cannot change does not exist, so every saved row has a menu. -->
{#snippet viewRow(view: SidebarView, hiddenRow: boolean)}
  {@const saved = view.saved}
  {@const actions = viewActions}
  {@const menu = actions && (view.builtIn || saved?.canEdit || (saved && !saved.mine))}
  <Sidebar.MenuItem>
    <Sidebar.MenuButton
      isActive={currentView === view.key}
      tooltipContent={[view.label, view.jobTitle].filter(Boolean).join(', ')}
      tooltipContentProps={view.jobTitle ? { hidden: false } : tooltipFor(view.key)}
      onclick={closeSheet}
      class={menu ? 'pr-8 pointer-coarse:pr-12' : undefined}
    >
      {#snippet child({ props })}
        <a href={view.href} {...props}>
          {#if view.icon}
            {@const Icon = view.icon}
            <Icon aria-hidden="true" />
          {/if}
          <!-- relative: the sr-only note is absolute and would escape the truncated label, widening the sidebar's scroll. -->
          <span {@attach truncation(view.key)} class="relative min-w-0 flex-1 truncate group-data-[collapsible=icon]:sr-only">{view.label}{#if saved?.shared}<span class="sr-only">, shared with the team</span>{/if}</span>
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
            <!-- A built-in view cannot be deleted, only hidden: the label says why there is no Delete (the owner's walk-through,
                 2026-11-21). -->
            <DropdownMenu.Label class="text-caption text-muted-foreground">Built-in view</DropdownMenu.Label>
            <DropdownMenu.Item onSelect={() => void run(actions.setHidden(view.key, !hiddenRow))}>
              {#if hiddenRow}<EyeIcon aria-hidden="true" />Show{:else}<EyeOffIcon aria-hidden="true" />Hide{/if}
            </DropdownMenu.Item>
          {:else if saved}
            {#if !saved.mine}
              <!-- A teammate's view the person added: leaving the sidebar keeps it shared, and Browse team views adds it again. -->
              <DropdownMenu.Item onSelect={() => void run(actions.removeFromSidebar(saved))}>
                <CircleMinusIcon aria-hidden="true" />Remove from sidebar
              </DropdownMenu.Item>
            {/if}
            {#if saved.canEdit}
              {#if !saved.mine}<DropdownMenu.Separator />{/if}
              <DropdownMenu.Item onSelect={() => (renameQueued = saved)}><PencilIcon aria-hidden="true" />Rename</DropdownMenu.Item>
              <DropdownMenu.Item onSelect={() => void run(actions.setShared(saved, !saved.shared))}>
                {#if saved.shared}<LockIcon aria-hidden="true" />Make private{:else}<UsersIcon aria-hidden="true" />Share with the team{/if}
              </DropdownMenu.Item>
              <DropdownMenu.Separator />
              <DropdownMenu.Item variant="destructive" onSelect={() => void run(actions.remove(saved))}>
                <Trash2Icon aria-hidden="true" />Delete view
              </DropdownMenu.Item>
            {/if}
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
