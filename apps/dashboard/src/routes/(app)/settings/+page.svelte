<script lang="ts">
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SettingsRows from '#lib/components/settings-rows.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { searchElsewhere, searchSettings, settingsGroups, visibleSettingsSections } from '#lib/settings.js';
  import { getShell } from '#lib/shell.js';
  import type { PageProps } from './$types';

  // The Settings home (design system section 11): every setting the person's role may use, in groups, with search. A
  // setting their role cannot use is left out, and so is a group with none left (A3). A row is the setting's name as a
  // link, and a line under it only where the name does not explain itself (W9). Searching swaps the groups for one list of
  // what matches, each with its group after the name, found by the same words as Ctrl K.
  let { data }: PageProps = $props();

  const shell = getShell();
  const visible = $derived(visibleSettingsSections(data.user, shell?.features()));
  const groups = $derived(settingsGroups(visible));
  const domainMark = $derived(shell?.sendingDomainMark());

  let query = $state('');
  const searching = $derived(query.trim() !== '');
  // What people look for here that lives in its own area (a scorecard, the AI model) follows the settings it found, with its area
  // where a setting has its group, so a search never ends where the thing is not.
  const found = $derived([...searchSettings(visible, query), ...searchElsewhere(data.user, query)]);
</script>

<svelte:head>
  <title>Settings · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }]} />

<main class="flex flex-col">
  <HeaderBand title="Settings" />

  <div class="flex w-full min-w-0 flex-col gap-5 px-4 py-6 md:px-6">
    <div role="search">
      <SearchField bind:value={query} label="Search settings" class="h-11 max-w-form" />
    </div>

    {#if searching}
      <!-- With nothing found the panel below says so, so the count is there only for screen readers (W7). -->
      <p role="status" class={['text-body text-muted-foreground', found.length === 0 && 'sr-only']}>
        {found.length === 1 ? '1 setting' : `${found.length} settings`}
      </p>
      <!-- One column of short rows, as wide as the field, so each row's group sits near its name. -->
      {#if found.length > 0}
        <Panel label="Settings that match" flush class="max-w-form">
          <SettingsRows list={found} withGroup {domainMark} />
        </Panel>
      {:else}
        <Panel label="Settings that match" class="max-w-form">
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>No setting matches "{query.trim()}".</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button variant="outline" size="lg" onclick={() => (query = '')}>Clear search</Button></Empty.Content>
          </Empty.Root>
        </Panel>
      {/if}
    {:else}
      <div class="grid items-start gap-5 md:grid-cols-2 xl:grid-cols-3">
        {#each groups as { group, sections } (group)}
          <Panel title={group} flush>
            <SettingsRows list={sections} {domainMark} />
          </Panel>
        {/each}
      </div>
    {/if}
  </div>
</main>
