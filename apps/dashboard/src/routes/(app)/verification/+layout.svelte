<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { can } from '#lib/permissions.js';
  import type { LayoutProps } from './$types';

  // The Verification area keeps the permission its page had as a Settings page (Edit settings, the API's RequirePermission,
  // ADR-0014). The sidebar offers it only to roles that have it; this answers anyone who opens it by its address.
  let { data, children }: LayoutProps = $props();

  const allowed = $derived(can(data.user, 'EditSettings'));
</script>

<svelte:head>
  {#if !allowed}<title>Verification · ScreenHire</title>{/if}
</svelte:head>

{#if allowed}
  {@render children()}
{:else}
  <TopBar crumbs={[{ label: 'Verification' }]} />

  <main class="flex flex-col">
    <HeaderBand form title="Verification" />
    <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
      <Panel title="Not in your role">
        <Empty.Root>
          <Empty.Header>
            <Empty.Description>Your role cannot change verification. Ask an owner or admin of your organization.</Empty.Description>
          </Empty.Header>
          <Empty.Content><Button href="/" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to jobs</Button></Empty.Content>
        </Empty.Root>
      </Panel>
    </div>
  </main>
{/if}
