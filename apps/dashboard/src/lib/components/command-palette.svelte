<script lang="ts">
  import { createSearch } from '@screenhire/api-client/svelte-query';
  import { keepPreviousData } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import FailureText from '#lib/components/failure-text.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Command from '#lib/components/ui/command/index.js';
  import { Kbd, KbdGroup } from '#lib/components/ui/kbd/index.js';
  import { jobStatus } from '#lib/job-status.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import { matchesWords, settingsHome, settingsSearchText, visibleSettingsSections } from '#lib/settings.js';
  import { getShell, sectionsFor } from '#lib/shell.js';
  import { keptInMindLabel } from '#lib/talent-pool.js';

  // Ctrl K / ⌘K: jump to a section, a job or a candidate, or run an action. Jobs and candidates come from the server's
  // search (`GET /v1/search`: names and emails, job titles), so it finds what no list has loaded, within the jobs the
  // person sees. It asks once typing pauses, from two characters, and keeps the last answer on screen meanwhile.
  let { open = $bindable(false) }: { open?: boolean } = $props();

  const shell = getShell();

  /** How long typing has to pause before the palette searches. */
  const debounceMs = 200;
  const minLength = 2;
  /** The API refuses a longer search (`SearchEndpoints.MaxLength`), so the input stops there. */
  const maxLength = 100;
  let text = $state('');
  let term = $state('');
  $effect(() => {
    const next = text.trim();
    const timer = setTimeout(() => (term = next), debounceMs);
    return () => clearTimeout(timer);
  });
  // Each opening starts empty.
  $effect(() => {
    if (!open) {
      text = '';
      term = '';
    }
  });

  const searching = $derived(open && term.length >= minLength);
  // No live event refreshes a search, so a term typed again asks again: a candidate added a moment ago turns up.
  const search = createSearch(
    () => ({ q: term }),
    () => ({ query: { enabled: searching, placeholderData: keepPreviousData, staleTime: 0 } }),
  );
  const found = $derived(searching ? search.data?.data : undefined);
  // Typing has not paused yet, or the answer is on its way: "Nothing matches" would be premature.
  const waiting = $derived(open && text.trim().length >= minLength && (term !== text.trim() || search.isFetching));

  /** Separates what a person can type from the id that keeps two rows with the same words apart. */
  const idMark = '\u001f';
  /** Ends the value of a Settings row, so `filter` reads the whole of its words. */
  const settingsMark = '\u001fsettings';

  /**
   * The palette's own filter, replacing cmdk's fuzzy one: "sen" matched every Settings page because their values begin
   * "Settings", which put real jobs below nine settings. A row matches when its words contain what was typed, best when
   * they start with it or a word does. The id after `idMark` is never searched, so two characters cannot hit a guid.
   */
  function filter(value: string, search: string): number {
    // A Settings row is found the way the Settings home's search finds it: every word typed is somewhere in its name,
    // description, keywords or group.
    if (value.endsWith(settingsMark)) return matchesWords(value.slice(0, -settingsMark.length), search) ? 0.9 : 0;
    const words = value.split(idMark)[0].toLowerCase();
    const query = search.trim().toLowerCase();
    if (query === '' || words.startsWith(query)) return 1;
    const at = words.indexOf(query);
    if (at < 0) return 0;
    return /[\s.@-]/.test(words[at - 1]) ? 0.8 : 0.5;
  }

  // Every Settings page the person may use, and the sections they may go to (Verification needs a permission).
  const settingsPages = $derived(visibleSettingsSections(page.data?.user, shell?.features()));
  const goTo = $derived(sectionsFor(page.data?.user));

  function run(action: () => unknown) {
    open = false;
    void action();
  }
</script>

<Command.Dialog bind:open {filter} title="Command palette" description="Jump to a job or candidate, or run an action.">
  <!-- aria-controls: the combobox role requires it, and bits-ui does not set it. -->
  <Command.Input
    bind:value={text}
    maxlength={maxLength}
    placeholder="Search jobs, candidates and actions…"
    aria-label="Search jobs, candidates and actions"
    aria-controls="command-palette-results"
  />
  <Command.List id="command-palette-results">
    {#if !waiting}<Command.Empty>Nothing matches.</Command.Empty>{/if}
    <!-- First, so what the server found for the words typed outranks the pages and actions that merely contain them. -->
    {#if found && found.jobs.length > 0}
      <Command.Group heading="Jobs">
        {#each found.jobs as job (job.id)}
          <Command.Item value={`${job.title}${idMark}${job.id}`} onSelect={() => run(() => goto(`/jobs/${job.id}`))}>
            {job.title}
            <span class="ml-auto"><StatusBadge {...jobStatus[job.status]} quiet /></span>
          </Command.Item>
        {/each}
      </Command.Group>
    {/if}
    {#if found && found.candidates.length > 0}
      <Command.Group heading="Candidates">
        {#each found.candidates as hit (hit.applicationId)}
          <!-- The email is in the value, so a hit the server found by email stays through the palette's own filter. -->
          <Command.Item
            value={`${hit.candidateName} ${hit.email} ${hit.jobTitle}${idMark}${hit.applicationId}`}
            onSelect={() => run(() => goto(`/applications/${hit.applicationId}`))}
          >
            {hit.candidateName}
            <span class="ml-auto flex min-w-0 items-center gap-2">
              <!-- An option cannot hold a tooltip, so a long title wraps rather than being cut (F5). -->
              <span class="min-w-0 text-right text-caption break-words text-muted-foreground">{hit.jobTitle}</span>
              {#if hit.keptInMindAt}
                <StatusBadge status="saved" label={keptInMindLabel} quiet />
              {:else}
                <Badge variant="muted">{hit.stage.name}</Badge>
              {/if}
            </span>
          </Command.Item>
        {/each}
      </Command.Group>
    {/if}
    <Command.Group heading="Go to">
      {#each goTo as section (section.href)}
        <Command.Item value={`Go to ${section.label}`} onSelect={() => run(() => goto(section.href))}>
          {section.label}
          {#if preferences.shortcutsEnabled}
            <Command.Shortcut><KbdGroup><Kbd>G</Kbd><Kbd>{section.key.toUpperCase()}</Kbd></KbdGroup></Command.Shortcut>
          {/if}
        </Command.Item>
      {/each}
      {#if settingsPages.length > 0}
        <Command.Item value="Go to Settings" onSelect={() => run(() => goto(settingsHome))}>Settings</Command.Item>
      {/if}
    </Command.Group>
    {#if settingsPages.length > 0}
      <Command.Group heading="Settings">
        <!-- The value holds the group's name, the description and the keywords, so "organization" finds Team and Roles and "logo" finds Branding. -->
        {#each settingsPages as setting (setting.href)}
          <Command.Item value={`${settingsSearchText(setting)}${settingsMark}`} onSelect={() => run(() => goto(setting.href))}>
            {setting.label}
            <span class="ml-auto text-caption text-muted-foreground">{setting.group}</span>
          </Command.Item>
        {/each}
      </Command.Group>
    {/if}
    {#if searching && search.isError}
      <FailureText role="status" class="px-3 py-2" message="Could not search jobs and candidates. Try again." />
    {:else if waiting && !found}
      <Command.Loading class="px-3 py-2 text-muted-foreground">Searching…</Command.Loading>
    {/if}
    <Command.Group heading="Actions">
      <Command.Item value="Switch theme" onSelect={() => run(() => preferences.toggleTheme())}>
        Switch to {preferences.theme === 'dark' ? 'light' : 'dark'} theme
      </Command.Item>
      <Command.Item value="Show keyboard shortcuts" onSelect={() => run(() => shell.openShortcuts())}>
        Show keyboard shortcuts
        {#if preferences.shortcutsEnabled}<Command.Shortcut><Kbd>?</Kbd></Command.Shortcut>{/if}
      </Command.Item>
      <Command.Item value="Show notification settings" onSelect={() => run(() => shell.openNotifications())}>Show notification settings</Command.Item>
      <Command.Item
        value="Single-key shortcuts"
        onSelect={() => run(() => preferences.setShortcutsEnabled(!preferences.shortcutsEnabled))}
      >
        Turn single-key shortcuts {preferences.shortcutsEnabled ? 'off' : 'on'}
      </Command.Item>
      <Command.Item value="Passkeys" onSelect={() => run(() => goto('/account/passkeys'))}>Manage passkeys</Command.Item>
      <Command.Item value="Sign out" onSelect={() => run(signOutAndRedirect)}>Sign out</Command.Item>
    </Command.Group>
  </Command.List>
</Command.Dialog>
