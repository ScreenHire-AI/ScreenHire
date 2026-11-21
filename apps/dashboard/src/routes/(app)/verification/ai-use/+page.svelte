<script lang="ts">
  import { modelProviderLabels } from '#lib/verification-settings.js';
  import Panel from '#lib/components/panel.svelte';
  import AiUsageLimitsForm from '#lib/components/ai-usage-limits-form.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import AiModelForm from '#lib/components/verification-area/ai-model-form.svelte';
  import AiUsageAlertForm from '#lib/components/verification-area/ai-usage-alert-form.svelte';
  import LimitsSummary from '#lib/components/verification-area/limits-summary.svelte';
  import SettingsTab from '#lib/components/verification-area/settings-tab.svelte';
  import UsageFigures from '#lib/components/verification-area/usage-figures.svelte';
  import { can } from '#lib/permissions.js';
  import { canEditVerification, canSeeUsage } from '#lib/verification-area.js';
  import type { PageProps } from './$types';

  // The AI use tab (docs/architecture/ai-usage.md): who pays for the AI check and how much. What the checks used, for who can
  // see AI usage; the monthly budget and cap, set by an Owner and read by an Admin who sees usage; the AI usage alert and whose
  // key runs the AI check, for Edit settings. The tab is for who has any of those (the tab's +page.ts), and each part checks
  // its own permission, so a part someone cannot use is left out, never shown disabled (A3).
  let { data }: PageProps = $props();

  const usage = $derived(canSeeUsage(data.user));
  const edit = $derived(canEditVerification(data.user));
  const setsLimits = $derived(can(data.user, 'ManageBilling'));
</script>

<svelte:head>
  <title>AI use · Verification · ScreenHire</title>
</svelte:head>

{#if usage}
  <UsageFigures />
{/if}

<SettingsTab title="AI use">
  {#snippet children(detail)}
    {#if detail.limits}
      <Panel title="Monthly limits">
        {#if setsLimits}
          <AiUsageLimitsForm limits={detail.limits} />
        {:else}
          <div class="flex flex-col gap-4">
            <LimitsSummary limits={detail.limits} />
            <p class="max-w-measure text-muted-foreground">An owner sets the budget and cap. They apply whichever key runs the checks.</p>
          </div>
        {/if}
      </Panel>
    {/if}
    {#if edit}
      <AiUsageAlertForm {detail} />
      <AiModelForm {detail} />
    {:else}
      <Panel title="Whose key runs the AI check">
        <SummaryList>
          <SummaryItem label="Key">
            {#if detail.modelSource === 'OwnKey'}
              Your own key{detail.provider ? `, ${modelProviderLabels[detail.provider]}` : ''}{detail.model ? `, ${detail.model}` : ''}. Your AI provider bills you.
            {:else}
              ScreenHire's key. You're billed for what the checks use.
            {/if}
          </SummaryItem>
        </SummaryList>
      </Panel>
    {/if}
  {/snippet}
</SettingsTab>
