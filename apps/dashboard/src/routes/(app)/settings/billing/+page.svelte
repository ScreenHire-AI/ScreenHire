<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import CreditCardIcon from '~icons/lucide/credit-card';
  import { createGetBilling, createOpenBillingPortal, createStartBillingCheckout } from '@screenhire/api-client/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { billingStatus, formatMoney, mayChoosePlan, openJobsText, planLimitText, priceLine } from '#lib/billing.js';
  import { describeError } from '#lib/query.js';

  // The plan, the trial or the next payment, and the open jobs against the plan's limit, for an owner (ManageBilling,
  // docs/architecture/billing.md). The form template (section 7). Payment details and plan changes happen on the payment
  // provider's own pages: Manage billing opens its portal, and a plan is chosen through Checkout. The browser comes back
  // from Checkout to `?checkout=success` or `?checkout=cancelled`.
  const billing = createGetBilling(() => ({ query: { refetchOnMount: 'always' } }));
  const overview = $derived(billing.data?.data);
  const checkout = createStartBillingCheckout();
  const portal = createOpenBillingPortal();

  // How a round trip to Checkout ended, read once and taken out of the address, so a reload does not repeat it.
  const returned = page.url.searchParams.get('checkout');
  const outcome = returned === 'success' || returned === 'cancelled' ? returned : null;
  $effect(() => {
    if (page.url.searchParams.has('checkout')) {
      const url = new URL(page.url.href);
      url.searchParams.delete('checkout');
      void goto(url, { replace: true, reset: false });
    }
  });

  // The provider's address is absolute for Stripe and a path of this site for the local sandbox. Both are left by
  // navigating, so a button stays busy once the address arrives, until the page is gone.
  let leaving = $state<string | null>(null);
  let failure = $state('');
  async function leave(target: string) {
    failure = '';
    leaving = target;
    try {
      const response = target === 'portal' ? await portal.mutateAsync() : await checkout.mutateAsync({ data: { plan: target } });
      window.location.assign(response.data.url);
    } catch (error) {
      failure = describeError(error, target === 'portal' ? 'Could not open the payment portal' : 'Could not start checkout');
      leaving = null;
    }
  }

  const standing = $derived(overview?.state ?? null);
  const subscribed = $derived(standing === 'Active' || standing === 'PastDue');
  const choosable = $derived(mayChoosePlan(standing));
</script>

<svelte:head>
  <title>Billing · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Organization' }, { label: 'Billing' }]} />

<main class="flex flex-col">
  <HeaderBand title="Billing" oneRow>
    {#snippet status()}
      {#if overview?.available && standing}<StatusBadge {...billingStatus[standing]} />{/if}
    {/snippet}
    {#snippet actions()}
      <!-- The one primary action, only while there is a subscription to manage. Before one, the plans below are equal choices (A1). -->
      {#if overview?.available && overview.canManage && subscribed}
        <Button size="lg" disabled={leaving !== null} onclick={() => leave('portal')}><CreditCardIcon aria-hidden="true" />Manage billing</Button>
      {/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      {#if billing.isError && !overview}
        <Panel title="Plan">
          <QueryError message={describeError(billing.error, 'Could not load billing')} onretry={() => billing.refetch()} inset />
        </Panel>
      {:else if !overview}
        <Panel title="Plan">
          <div aria-hidden="true" class="flex flex-col gap-3">
            {#each [0, 1, 2] as row (row)}<Skeleton class="h-5 w-full" />{/each}
          </div>
          <p class="sr-only" role="status">Loading billing…</p>
        </Panel>
        <Panel title="Plans">
          <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-12 w-full" /><Skeleton class="h-12 w-full" /></div>
        </Panel>
      {:else if !overview.available}
        <Panel title="Plan">
          <Empty.Root>
            <Empty.Header>
              <Empty.Title>Billing is not set up here</Empty.Title>
              <Empty.Description>It needs a payment provider where ScreenHire is hosted. Nothing is limited until it is.</Empty.Description>
            </Empty.Header>
          </Empty.Root>
        </Panel>
      {:else}
        {#if standing === 'PastDue'}
          <Notice>Your last payment failed. Update your payment details to keep publishing.</Notice>
        {:else if standing === 'Ended'}
          <!-- The badge beside the title says Ended, for a subscription or a trial that ran out alike. -->
          <Notice>Open jobs stay open, but you cannot publish new jobs until you choose a plan.</Notice>
        {/if}
        <FailureText message={failure} />
        {#if billing.isError}<FailureText message={describeError(billing.error, 'Could not refresh billing')} />{/if}

        <Panel title="Plan" footer={outcome ? returnedFooter : undefined}>
          <SummaryList>
            <SummaryItem label="Current plan">{overview.currentPlan?.name ?? 'No plan'}</SummaryItem>
            {#if standing === 'Trialing' && overview.trialEndsAt}
              <SummaryItem label="Trial ends"><DateText at={overview.trialEndsAt} /></SummaryItem>
            {/if}
            {#if overview.cancelsAtPeriodEnd && overview.nextPaymentAt}
              <SummaryItem label="Ends on"><DateText at={overview.nextPaymentAt} /></SummaryItem>
            {:else if overview.nextPaymentAt}
              <SummaryItem label="Next payment">
                {#if overview.nextPaymentAmount !== null && overview.currentPlan}
                  {formatMoney(overview.nextPaymentAmount, overview.currentPlan.currency)} on
                {/if}
                <DateText at={overview.nextPaymentAt} />
              </SummaryItem>
            {/if}
            <SummaryItem label="Open jobs">{openJobsText(overview)}</SummaryItem>
          </SummaryList>
        </Panel>

        <Panel title="Plans" footer={subscribed && overview.canManage ? changeFooter : undefined}>
          <ul class="-my-3">
            {#each overview.plans as plan (plan.key)}
              <li class="flex flex-wrap items-center gap-x-4 gap-y-2 border-b border-border-subtle py-3 last:border-0">
                <div class="flex min-w-0 flex-[1_1_12rem] flex-col gap-0.5">
                  <p class="font-semibold">{plan.name}</p>
                  <p class="text-muted-foreground">{priceLine(plan, overview.taxAtCheckout)}</p>
                  <p class="text-muted-foreground">{planLimitText(plan)}</p>
                </div>
                <!-- A plan change on a live subscription goes through the portal; Checkout is for starting one. -->
                {#if choosable && plan.canSubscribe}
                  <Button variant="outline" size="lg" disabled={leaving !== null} onclick={() => leave(plan.key)}>
                    Choose{' '}<span class="sr-only">{plan.name}</span><ArrowRightIcon aria-hidden="true" />
                  </Button>
                {/if}
              </li>
            {/each}
          </ul>
        </Panel>
      {/if}
    </div>
  </SettingsMenu>
</main>

{#snippet returnedFooter()}
  <p role="status">
    {outcome === 'success' ? 'Checkout finished. This page updates when the payment is confirmed.' : 'Checkout was cancelled. You have not been charged.'}
  </p>
{/snippet}

{#snippet changeFooter()}
  <p class="text-muted-foreground">To change your plan, use Manage billing.</p>
{/snippet}
