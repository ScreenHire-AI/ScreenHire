<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import type { BillingPlan } from '@screenhire/api-client';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { formatMoney, planIncludes, planLimitText } from '#lib/billing.js';
  import { cn } from '#lib/utils.js';

  // The plans as comparison cards side by side (design system section 11, Plan comparison cards), in the catalogue's order, the
  // order of the marketing pricing page: each card's name, its price, its open job limit first and then what every plan includes,
  // from the plan catalogue the pricing page reads. The plan in force is marked by its words and the primary edge; the others
  // carry the page's action for them (`actionFor`), the same Choose or Change to as before. The cards sit in the Plans panel, so
  // they are bordered with no shadow (S4, S5), three across once the panel is wide enough and one above the other below that, never
  // two and one.
  let {
    plans,
    current = null,
    currentLabel = 'Your plan',
    taxAtCheckout = false,
    actionFor,
    disabled = false,
  }: {
    plans: BillingPlan[];
    /** The key of the plan to mark, if any. */
    current?: string | null;
    /** How the marked plan is named: Your plan, or Your trial while the trial runs on it. */
    currentLabel?: string;
    taxAtCheckout?: boolean;
    /** The button for a plan, or null for none. */
    actionFor: (plan: BillingPlan) => { label: string; run: () => void } | null;
    disabled?: boolean;
  } = $props();
</script>

<div class="@container">
  <ul class="grid grid-cols-1 gap-3 @lg:grid-cols-3">
    {#each plans as plan (plan.key)}
      {@const marked = plan.key === current}
      {@const action = actionFor(plan)}
      <li
        class={cn('flex min-w-0 flex-col gap-3 rounded-lg border p-4', marked ? 'border-primary ring-1 ring-primary' : 'border-input')}
        data-plan-card={plan.key}
      >
        <div class="flex min-w-0 flex-wrap items-center justify-between gap-2">
          <h3 class="min-w-0 truncate text-heading">{plan.name}</h3>
          {#if marked}<Badge variant="muted">{currentLabel}</Badge>{/if}
        </div>
        {#if plan.monthlyPrice === null}
          <p class="text-muted-foreground">Price not set</p>
        {:else}
          <p class="flex flex-col">
            <span><span class="text-metric tabular-nums">{formatMoney(plan.monthlyPrice, plan.currency)}</span> <span class="text-muted-foreground">a month</span></span>
            {#if taxAtCheckout}<span class="text-caption text-muted-foreground">plus tax where it applies</span>{/if}
          </p>
        {/if}
        <ul aria-label="What {plan.name} includes" class="flex flex-col gap-1.5">
          <li class="flex items-start gap-1.5 font-semibold"><CheckIcon aria-hidden="true" class="mt-0.5 shrink-0" />{planLimitText(plan)}</li>
          {#each planIncludes as item (item)}
            <li class="flex items-start gap-1.5 text-muted-foreground"><CheckIcon aria-hidden="true" class="mt-0.5 shrink-0" />{item}</li>
          {/each}
        </ul>
        {#if action}
          <Button variant="outline" size="lg" class="mt-auto w-full" {disabled} onclick={action.run}>{action.label}</Button>
        {/if}
      </li>
    {/each}
  </ul>
</div>
