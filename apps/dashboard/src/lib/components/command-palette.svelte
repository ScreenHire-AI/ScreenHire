<script lang="ts">
  import { createSearch } from '@screenhire/api-client/svelte-query';
  import { keepPreviousData } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Command from '#lib/components/ui/command/index.js';
  import { Kbd, KbdGroup } from '#lib/components/ui/kbd/index.js';
  import { jobStatus } from '#lib/job-status.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import { visibleSettingsSections } from '#lib/settings.js';
  import { getShell, sections } from '#lib/shell.js';

  // Ctrl K / ⌘K: jump to a section, a job or a candidate, or run an action. Jobs and candidates come from the server's
  // search (`GET /v1/search`: names and emails, job titles), so it finds what no list has loaded, within the jobs the
  // person sees. It asks once typing pauses, from two characters, and keeps the last answer on screen meanwhile.
  let { open = $bindable(false) }: { open?: boolean } = $props();

  const shell = getShell();

  /** How long typing has to pause before the palette searches. */
  const debounceMs = 200;
  const minLength = 2;
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
  const search = createSearch(
    () => ({ q: term }),
    () => ({ query: { enabled: searching, placeholderData: keepPreviousData, staleTime: 30_000 } }),
  );
  const found = $derived(searching ? search.data?.data : undefined);
  // Typing has not paused yet, or the answer is on its way: "Nothing matches" would be premature.
  const waiting = $derived(open && text.trim().length >= minLength && (term !== text.trim() || search.isFetching));

  function run(action: () => unknown) {
    open = false;
    void action();
  }
</script>

<Command.Dialog bind:open title="Command palette" description="Jump to a job or candidate, or run an action.">
  <!-- aria-controls: the combobox role requires it, and bits-ui does not set it. -->
  <Command.Input
    bind:value={text}
    placeholder="Search jobs, candidates and actions…"
    aria-label="Search jobs, candidates and actions"
    aria-controls="command-palette-results"
  />
  <Command.List id="command-palette-results">
    {#if !waiting}<Command.Empty>Nothing matches.</Command.Empty>{/if}
    <Command.Group heading="Go to">
      {#each sections as section (section.href)}
        <Command.Item value={`Go to ${section.label}`} onSelect={() => run(() => goto(section.href))}>
          {section.label}
          {#if preferences.shortcutsEnabled}
            <Command.Shortcut><KbdGroup><Kbd>G</Kbd><Kbd>{section.key.toUpperCase()}</Kbd></KbdGroup></Command.Shortcut>
          {/if}
        </Command.Item>
      {/each}
    </Command.Group>
    {#if visibleSettingsSections(page.data?.user).length > 0}
      <Command.Group heading="Settings">
        {#each visibleSettingsSections(page.data?.user) as setting (setting.href)}
          <Command.Item value={`Settings ${setting.label}`} onSelect={() => run(() => goto(setting.href))}>{setting.label}</Command.Item>
        {/each}
      </Command.Group>
    {/if}
    {#if found && found.jobs.length > 0}
      <Command.Group heading="Jobs">
        {#each found.jobs as job (job.id)}
          <Command.Item value={`Job ${job.title} ${job.id}`} onSelect={() => run(() => goto(`/jobs/${job.id}`))}>
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
            value={`Candidate ${hit.candidateName} ${hit.email} ${hit.jobTitle} ${hit.applicationId}`}
            onSelect={() => run(() => goto(`/applications/${hit.applicationId}`))}
          >
            {hit.candidateName}
            <span class="ml-auto flex min-w-0 items-center gap-2">
              <!-- An option cannot hold a tooltip, so a long title wraps rather than being cut (F5). -->
              <span class="min-w-0 text-right text-caption break-words text-muted-foreground">{hit.jobTitle}</span>
              <Badge variant="muted">{hit.stage.name}</Badge>
            </span>
          </Command.Item>
        {/each}
      </Command.Group>
    {/if}
    {#if searching && search.isError}
      <p role="status" class="px-3 py-2 text-muted-foreground">Could not search jobs and candidates. Try again.</p>
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
      <Command.Item
        value="Single-key shortcuts"
        onSelect={() => run(() => preferences.setShortcutsEnabled(!preferences.shortcutsEnabled))}
      >
        Turn single-key shortcuts {preferences.shortcutsEnabled ? 'off' : 'on'}
      </Command.Item>
      <Command.Item value="Sign out" onSelect={() => run(signOutAndRedirect)}>Sign out</Command.Item>
    </Command.Group>
  </Command.List>
</Command.Dialog>
