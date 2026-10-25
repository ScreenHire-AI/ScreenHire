<script lang="ts">
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { page } from '$app/state';
  import { can } from '#lib/permissions.js';
  import { settingsSectionOf, visibleSettingsSections } from '#lib/settings.js';
  import type { LayoutProps } from './$types';

  // Each settings page needs the permission its changes need (the API's RequirePermission, ADR-0014). The sidebar offers
  // only the pages the person's role may use; this answers anyone who opens another by its address, instead of a page of
  // refused requests.
  let { data, children }: LayoutProps = $props();

  const section = $derived(settingsSectionOf(page.url.pathname));
  const allowed = $derived(section ? can(data.user, section.permission) : visibleSettingsSections(data.user).length > 0);
</script>

<svelte:head>
  {#if !allowed}<title>Settings · ScreenHire</title>{/if}
</svelte:head>

{#if allowed}
  {@render children()}
{:else}
  <TopBar crumbs={[{ label: 'Settings' }]} />

  <main class="flex flex-col">
    <HeaderBand form title="Settings" />
    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      <Panel title="Not in your role">
        <Empty.Root>
          <Empty.Header>
            <Empty.Description>Your role cannot change {section ? section.label.toLowerCase() : 'settings'}. Ask an owner or admin of your organization.</Empty.Description>
          </Empty.Header>
          <Empty.Content><Button href="/" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to jobs</Button></Empty.Content>
        </Empty.Root>
      </Panel>
    </div>
  </main>
{/if}
