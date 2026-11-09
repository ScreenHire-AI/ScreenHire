<script lang="ts">
  import Panel from '#lib/components/panel.svelte';
  import AlertsForm from '#lib/components/verification-area/alerts-form.svelte';
  import LimitsSummary from '#lib/components/verification-area/limits-summary.svelte';
  import SettingsTab from '#lib/components/verification-area/settings-tab.svelte';
  import AiUsageLimitsForm from '#lib/components/ai-usage-limits-form.svelte';
  import { can } from '#lib/permissions.js';
  import type { PageProps } from './$types';

  // The Notifications tab: the two alerts, and the monthly budget and cap they report on. Only an Owner sets the limits;
  // an Admin who can see AI usage reads them. Thresholds are fixed, so they are text.
  let { data }: PageProps = $props();
  const canSetLimits = $derived(can(data.user, 'ManageBilling'));
</script>

<svelte:head>
  <title>Notifications · Verification · ScreenHire</title>
</svelte:head>

<SettingsTab title="Alerts">
  {#snippet children(detail)}
    <AlertsForm {detail} />
    {#if detail.limits}
      <Panel title="Monthly limits">
        {#if canSetLimits}
          <AiUsageLimitsForm limits={detail.limits} />
        {:else}
          <div class="flex flex-col gap-4">
            <p class="max-w-measure text-muted-foreground">An owner sets the budget and cap.</p>
            <LimitsSummary limits={detail.limits} />
          </div>
        {/if}
      </Panel>
    {/if}
  {/snippet}
</SettingsTab>
