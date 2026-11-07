<script lang="ts">
  import PlugIcon from '~icons/lucide/plug';
  import { page } from '$app/state';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IntegrationEntry from '#lib/components/integration-entry.svelte';
  import Panel from '#lib/components/panel.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { filterIntegrations, integrationKinds, searchThreshold } from '#lib/integrations.js';
  import { visibleIntegrations } from '#lib/settings.js';
  import { getShell } from '#lib/shell.js';

  // The Integrations directory (design system S9 and section 11): every integration as a row with its icon, one line on
  // what it does and whether it is set up, and a press opens its page. Anything that can grow is a list, so the 20th
  // integration is one more entry in `#lib/integrations.ts` and no change here: search appears past ten, and the kinds
  // filter once there is more than one. The pages it opens keep their own addresses.
  const shell = getShell();
  const user = $derived(page.data.user);
  const all = $derived(visibleIntegrations(user, shell?.features()));
  const kinds = $derived(integrationKinds(all));
  let search = $state('');
  let kind = $state<string | null>(null);
  const shown = $derived(filterIntegrations(all, search, kinds.includes(kind ?? '') ? kind : null));
  const filtering = $derived(search.trim() !== '' || kind !== null);

  function clearFilters() {
    search = '';
    kind = null;
  }
</script>

<svelte:head>
  <title>Integrations · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Integrations' }]} />

<main>
  <HeaderBand title="Integrations" count={all.length} countLabel={all.length === 1 ? 'integration' : 'integrations'} />

  <SettingsMenu current="/settings/integrations">
    <Panel title="Directory" flush>
      {#snippet tools()}
        {#if all.length > searchThreshold}
          <SearchField bind:value={search} label="Search integrations" placeholder="Search" class="ml-auto flex-[0_1_15rem] max-md:ml-0 max-md:flex-1" />
        {/if}
      {/snippet}

      <!-- One line that scrolls inside the panel rather than wrapping (F5), each kind with its count (T4). -->
      {#if kinds.length > 1}
        <div class="border-b border-border-subtle px-4.5 py-2 max-md:px-4">
          <div class="min-w-0 overflow-x-auto [scrollbar-width:thin]">
            <div role="group" aria-label="Kind" class="flex w-max gap-1">
              <Toggle size="lg" bind:pressed={() => kind === null, () => (kind = null)}>
                All
              </Toggle>
              {#each kinds as option (option)}
                <Toggle size="lg" bind:pressed={() => kind === option, () => (kind = option)}>
                  {option} <Badge variant="count">{all.filter((integration) => integration.kind === option).length}</Badge>
                </Toggle>
              {/each}
            </div>
          </div>
        </div>
      {/if}

      {#if shown.length === 0}
        <Empty.Root>
          <Empty.Header>
            <Empty.Media variant="icon"><PlugIcon /></Empty.Media>
            <Empty.Title>{filtering ? 'No integrations match' : 'No integrations yet'}</Empty.Title>
          </Empty.Header>
          {#if filtering}<Empty.Content><Button variant="outline" size="lg" onclick={clearFilters}>Show all</Button></Empty.Content>{/if}
        </Empty.Root>
      {:else}
        <ul aria-label="Integrations">
          {#each shown as integration (integration.href)}
            <IntegrationEntry {integration} {user} />
          {/each}
        </ul>
      {/if}
    </Panel>
  </SettingsMenu>
</main>
