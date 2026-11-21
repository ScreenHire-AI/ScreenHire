<script lang="ts">
  import type { AiUsageLimitsView, AiUsageLimitView } from '@screenhire/api-client';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { budgetStatus, formatAmount } from '#lib/ai-usage.js';

  // This month against the AI budget and cap (docs/architecture/ai-usage.md): how much is used of each, and where it stands,
  // in words and an icon as well as a colour (design system 3.2). Nothing shows for a limit that is not set.
  // `cap` false leaves the cap out, for the Overview, which names the budget and leaves the cap to Usage and Notifications (W6).
  let { limits, cap = true }: { limits: AiUsageLimitsView; cap?: boolean } = $props();

  const line = (limit: AiUsageLimitView) => `${formatAmount(limit.used, limit.unit)} of ${formatAmount(limit.amount, limit.unit)}`;
  // ScreenHire's default counts tokens; the API shows it in dollars at the usual mix where the model has a price, so the figures
  // are an estimate and can differ from the Usage tab's actual cost. Said once, under the limits (ai-usage.md, "Defaults in dollars").
  const estimated = $derived([limits.effectiveBudget, cap ? limits.effectiveCap : null].some((limit) => limit?.isDefault && limit.unit === 'Money'));
</script>

{#if limits.effectiveBudget || (cap && limits.effectiveCap)}
  <SummaryList>
    {#if limits.effectiveBudget}
      {@const budget = limits.effectiveBudget}
      {@const state = budgetStatus(budget)}
      <SummaryItem label="Monthly budget">
        <span class="flex flex-wrap items-center gap-2">
          <span class="tabular-nums">{line(budget)}</span>
          <StatusBadge status={state.status} label={state.label} quiet={state.status === 'done'} />
          {#if !budget.isDefault}<span class="text-muted-foreground">Set by you</span>{/if}
        </span>
      </SummaryItem>
    {/if}
    {#if cap && limits.effectiveCap}
      {@const capLimit = limits.effectiveCap}
      {@const state = budgetStatus(capLimit)}
      <SummaryItem label="Monthly cap">
        <span class="flex flex-wrap items-center gap-2">
          <span class="tabular-nums">{line(capLimit)}</span>
          <StatusBadge status={state.status} label={capLimit.percent >= 100 ? 'Reached, AI checks paused' : state.label} quiet={state.status === 'done'} />
          {#if !capLimit.isDefault}<span class="text-muted-foreground">Set by you</span>{/if}
        </span>
      </SummaryItem>
    {/if}
  </SummaryList>
  {#if estimated}
    <p class="mt-2 text-caption text-muted-foreground" data-estimate>
      ScreenHire's default counts tokens. In dollars it is an estimate at the usual mix of input and output.
    </p>
  {/if}
{:else}
  <p class="text-muted-foreground">No budget or cap is set.</p>
{/if}
