<script lang="ts">
  import { createListApplications, createListJobs } from '@screenhire/api-client/svelte-query';
  import { goto } from '$app/navigation';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Command from '#lib/components/ui/command/index.js';
  import { Kbd, KbdGroup } from '#lib/components/ui/kbd/index.js';
  import { jobStatus } from '#lib/job-status.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import { getShell, sections } from '#lib/shell.js';

  // Ctrl K / ⌘K: jump to a section, a job or a candidate, or run an action. Lists load when it opens.
  let { open = $bindable(false) }: { open?: boolean } = $props();

  const shell = getShell();

  const jobs = createListJobs(() => ({ query: { enabled: open } }));
  const applications = createListApplications(() => ({ query: { enabled: open } }));

  function run(action: () => unknown) {
    open = false;
    void action();
  }
</script>

<Command.Dialog bind:open title="Command palette" description="Jump to a job or candidate, or run an action.">
  <!-- aria-controls: the combobox role requires it, and bits-ui does not set it. -->
  <Command.Input
    placeholder="Search jobs, candidates and actions…"
    aria-label="Search jobs, candidates and actions"
    aria-controls="command-palette-results"
  />
  <Command.List id="command-palette-results">
    <Command.Empty>Nothing matches.</Command.Empty>
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
    {#if jobs.data && jobs.data.data.length > 0}
      <Command.Group heading="Jobs">
        {#each jobs.data.data as job (job.id)}
          <Command.Item value={`Job ${job.title} ${job.id}`} onSelect={() => run(() => goto(`/jobs/${job.id}`))}>
            {job.title}
            <span class="ml-auto"><StatusBadge {...jobStatus[job.status]} quiet /></span>
          </Command.Item>
        {/each}
      </Command.Group>
    {/if}
    {#if applications.data && applications.data.data.length > 0}
      <Command.Group heading="Candidates">
        {#each applications.data.data as application (application.id)}
          <Command.Item
            value={`Candidate ${application.candidateName} ${application.jobTitle} ${application.id}`}
            onSelect={() => run(() => goto(`/applications/${application.id}`))}
          >
            {application.candidateName}
            <span class="ml-auto flex min-w-0 items-center gap-2">
              <!-- An option cannot hold a tooltip, so a long title wraps rather than being cut (F5). -->
              <span class="min-w-0 text-right text-caption break-words text-muted-foreground">{application.jobTitle}</span>
              <Badge variant="muted">{application.stage.name}</Badge>
            </span>
          </Command.Item>
        {/each}
      </Command.Group>
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
