<script lang="ts">
  import { createGetOrganizationVerificationSettings, getGetOrganizationVerificationSettingsHistoryQueryOptions } from '@screenhire/api-client/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import RecordTabs from '#lib/components/record-tabs.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { tabOfPath, tabsFor, verificationTab, type VerificationTabValue } from '#lib/verification-area.js';
  import type { LayoutProps } from './$types';

  // The Verification area (design system section 7, Area): one header band and one tab strip for its pages, then the tab's
  // own page under it. Everyone who sees verification has the area; each tab is in the strip only for a role that may use it,
  // never shown disabled (A3), and a tab's address checks the same (the tab's +page.ts). The four tabs that show the saved
  // settings carry Last updated and History on the title's row, so the band is one height on every tab (S8).
  let { data, children }: LayoutProps = $props();

  const visibleTabs = $derived(tabsFor(data.user));
  const current = $derived(tabOfPath(page.url.pathname));
  const settings = createGetOrganizationVerificationSettings(() => ({ query: { enabled: current.settings } }));
  const detail = $derived(settings.data?.data);

  const select = (value: string) => {
    if (value !== current.value) void goto(verificationTab(value as VerificationTabValue).href);
  };
</script>

<TopBar crumbs={[{ label: 'Verification' }]} />

<main class="flex flex-col">
  <Tabs.Root value={current.value} onValueChange={select}>
    <HeaderBand title="Verification" oneRow>
      {#snippet actions()}
        {#if current.settings && detail}
          <HistoryFact value={detail.lastUpdated} name="Verification settings" page={(before) => getGetOrganizationVerificationSettingsHistoryQueryOptions({ before })} />
        {/if}
      {/snippet}
      {#snippet tabs()}<RecordTabs tabs={visibleTabs} label="Verification pages" />{/snippet}
    </HeaderBand>

    <Tabs.Content value={current.value} class="@container flex w-full min-w-0 flex-col gap-5 px-4 py-6 md:px-6">
      {@render children()}
    </Tabs.Content>
  </Tabs.Root>
</main>
