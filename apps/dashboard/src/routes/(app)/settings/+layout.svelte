<script lang="ts">
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { isAdministrator } from '#lib/settings.js';
  import type { LayoutProps } from './$types';

  // Settings are for Owners and Admins (the API's RequireOrganizationAdmin). The sidebar shows them to nobody else; this
  // answers anyone who opens one by its address, instead of a page of refused requests.
  let { data, children }: LayoutProps = $props();

  const allowed = $derived(isAdministrator(data.user?.organization?.role));
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
      <Panel title="Owners and admins only">
        <Empty.Root>
          <Empty.Header>
            <Empty.Description>Ask an owner or admin of your organization to change its settings.</Empty.Description>
          </Empty.Header>
          <Empty.Content><Button href="/" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to jobs</Button></Empty.Content>
        </Empty.Root>
      </Panel>
    </div>
  </main>
{/if}
