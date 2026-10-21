<script lang="ts">
  import { createListApplications, createListJobs } from '@screenhire/api-client/svelte-query';
  import { goto } from '$app/navigation';
  import * as Command from '#lib/components/ui/command/index.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { signOutAndRedirect } from '#lib/session.js';
  import { sections } from '#lib/shell.js';

  // Ctrl K / ⌘K: jump to a section, a job or a candidate, or run an action. Lists load when it opens.
  let { open = $bindable(false) }: { open?: boolean } = $props();

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
          <Command.Shortcut>G {section.key.toUpperCase()}</Command.Shortcut>
        </Command.Item>
      {/each}
    </Command.Group>
    {#if jobs.data && jobs.data.data.length > 0}
      <Command.Group heading="Jobs">
        {#each jobs.data.data as job (job.id)}
          <Command.Item value={`Job ${job.title} ${job.id}`} onSelect={() => run(() => goto(`/jobs/${job.id}`))}>
            {job.title}
            <span class="ml-auto text-caption text-muted-foreground">{job.status}</span>
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
            <span class="ml-auto truncate text-caption text-muted-foreground">{application.jobTitle} · {application.stage.name}</span>
          </Command.Item>
        {/each}
      </Command.Group>
    {/if}
    <Command.Group heading="Actions">
      <Command.Item value="Switch theme" onSelect={() => run(() => preferences.toggleTheme())}>
        Switch to {preferences.theme === 'dark' ? 'light' : 'dark'} theme
      </Command.Item>
      <Command.Item
        value="Keyboard shortcuts"
        onSelect={() => run(() => preferences.setShortcutsEnabled(!preferences.shortcutsEnabled))}
      >
        Turn single-key shortcuts {preferences.shortcutsEnabled ? 'off' : 'on'}
      </Command.Item>
      <Command.Item value="Sign out" onSelect={() => run(signOutAndRedirect)}>Sign out</Command.Item>
    </Command.Group>
  </Command.List>
</Command.Dialog>
