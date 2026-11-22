<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import type { BillingOverview } from '@screenhire/api-client';
  import type { Snippet } from 'svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { formatMoney, headroomText, planIncludes, trialLeftText } from '#lib/billing.js';
  import { formatDayMonth } from '#lib/dates.js';

  // The plan card at the top of Billing (design system section 11, Plan card; docs/architecture/billing.md, "The Billing page"):
  // what the organization pays for most, with presence. The plan's name and monthly price large, when it renews or ends in words,
  // what every plan includes as a row of ticks from the plan catalogue, the open jobs against the limit, and the one way to change
  // it. During the trial the card is the trial: the days left, the trial's plan and Choose a plan. `onchoose` is the page's: it
  // brings the plans below into view, where a plan is chosen or changed as before, and the card offers it only when one can be.
  let {
    overview,
    now,
    onchoose,
    footer,
  }: {
    overview: BillingOverview;
    /** The clock the trial's days left are counted from. */
    now: number;
    /** Shown as Change plan (a subscription) or Choose a plan (the trial, or after it ended); left out when undefined. */
    onchoose?: () => void;
    footer?: Snippet;
  } = $props();

  const plan = $derived(overview.currentPlan);
  const state = $derived(overview.state);
  const trialing = $derived(state === 'Trialing' && Boolean(overview.trialEndsAt));
  const subscribed = $derived(state === 'Active' || state === 'PastDue');
  const headline = $derived(trialing && overview.trialEndsAt ? trialLeftText(overview.trialEndsAt, now) : (plan?.name ?? 'No plan'));
  // The price is what a subscription pays; on the trial nothing is charged, so it stays on the plans below.
  const price = $derived(subscribed && plan?.monthlyPrice != null ? formatMoney(plan.monthlyPrice, plan.currency) : null);
  // The date in words (section 13): when the subscription renews, or ends when it is set to. A failed payment says nothing here:
  // the badge beside the title and the notice above say what to do.
  const when = $derived.by(() => {
    if (trialing && overview.trialEndsAt) return { words: `${plan?.name ?? 'Your plan'} until`, at: overview.trialEndsAt };
    if (state === 'Active' && overview.nextPaymentAt) return { words: overview.cancelsAtPeriodEnd ? 'Ends' : 'Renews', at: overview.nextPaymentAt };
    return null;
  });
  const limit = $derived(overview.activeJobLimit);
  // The meter is a picture of the words beside it, so screen readers read the words. A downgrade can leave more open than the
  // limit: the bar stops full and the words say how many.
  const fill = $derived(limit === null ? 0 : limit <= 0 ? 1 : Math.min(1, overview.openJobs / limit));
</script>

<Panel title="Plan" {footer}>
  <div class="flex flex-col gap-5">
    <div class="flex flex-wrap items-end justify-between gap-x-6 gap-y-2">
      <div class="flex min-w-0 flex-col gap-1">
        <p class="font-display text-panel-title" data-plan-headline>{headline}</p>
        {#if when}
          <p class="text-muted-foreground">{when.words} <time datetime={when.at}>{formatDayMonth(when.at)}</time></p>
        {/if}
      </div>
      {#if price}
        <p class="flex flex-col items-start" data-plan-price>
          <span><span class="text-metric tabular-nums">{price}</span> <span class="text-muted-foreground">a month</span></span>
          {#if overview.taxAtCheckout}<span class="text-caption text-muted-foreground">plus tax where it applies</span>{/if}
        </p>
      {/if}
    </div>

    {#if plan}
      <ul aria-label="What your plan includes" class="flex flex-wrap gap-x-5 gap-y-1.5">
        {#each planIncludes as item (item)}
          <li class="flex items-center gap-1.5"><CheckIcon aria-hidden="true" class="shrink-0 text-muted-foreground" />{item}</li>
        {/each}
      </ul>
    {/if}

    <div class="flex flex-wrap items-center justify-between gap-x-6 gap-y-3 border-t border-border-subtle pt-4">
      <div class="flex min-w-0 flex-[1_1_14rem] flex-col gap-1.5">
        {#if limit !== null}
          <div aria-hidden="true" class="h-2 w-full max-w-xs overflow-hidden rounded-full bg-secondary" data-headroom-meter>
            <div class="h-full rounded-full bg-primary" style:width={`${fill * 100}%`}></div>
          </div>
        {/if}
        <p class="tabular-nums" data-headroom>{headroomText(overview)}</p>
      </div>
      {#if onchoose}
        <Button variant="outline" size="lg" onclick={onchoose}>{subscribed ? 'Change plan' : 'Choose a plan'}</Button>
      {/if}
    </div>
  </div>
</Panel>
