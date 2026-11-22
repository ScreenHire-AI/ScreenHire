<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import CreditCardIcon from '~icons/lucide/credit-card';
  import type { BillingPlan, BillingPortalIntent } from '@screenhire/api-client';
  import {
    createGetBilling,
    createGetBillingPaymentMethod,
    createGetOrganizationVerificationSettings,
    createOpenBillingPortal,
    createStartBillingCheckout,
    getGetBillingQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { useIsFetching } from '@tanstack/svelte-query';
  import { tick } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import AiUsageFigures from '#lib/components/ai-usage-figures.svelte';
  import AiUsageLimitsForm from '#lib/components/ai-usage-limits-form.svelte';
  import BillingInvoices from '#lib/components/billing-invoices.svelte';
  import ChangePlanDialog from '#lib/components/change-plan-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import { settingsCrumbs } from '#lib/settings.js';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import LimitsSummary from '#lib/components/verification-area/limits-summary.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { formatCost } from '#lib/ai-usage.js';
  import { aiCallsText, aiMonthText, aiUsageNotes, billingStatus, formatMoney, mayChoosePlan, openJobsText, paymentMethodText, planLimitText, priceLine } from '#lib/billing.js';
  import { can } from '#lib/permissions.js';
  import { describeError } from '#lib/query.js';
  import { aiModelAddress, canEditVerification, canSeeUsage } from '#lib/verification-area.js';
  import { modelProviderLabels } from '#lib/verification-settings.js';

  // Everything the organization pays, in one place (docs/architecture/billing.md). For an Owner (ManageBilling): the plan and open
  // jobs, this month so far added up with the next invoice, the payment method, the invoices and the plans, in the form template
  // (section 7). Then the AI use section, for whoever can see AI usage (ai-usage.md): what the AI check costs this month and on
  // whose key, what the checks used (a report at the full width), and the monthly spending limits, which an Owner sets. Someone
  // who sees AI usage without managing billing gets only that section, and so does an Owner where no payment provider is set
  // up (A3). The card and cancelling happen on the payment provider's own pages (Change card and Cancel plan open its portal)
  // and a first plan is chosen through Checkout; the browser comes back from Checkout to `?checkout=success` or
  // `?checkout=cancelled`. Switching plans on a live subscription is done here, after the dialog has shown what it costs. The
  // provider is never named.
  const user = $derived(page.data.user);
  const owner = $derived(can(user, 'ManageBilling'));
  const usage = $derived(canSeeUsage(user));
  const editsModel = $derived(canEditVerification(user));
  const billing = createGetBilling(() => ({ query: { enabled: owner, refetchOnMount: 'always' } }));
  const overview = $derived(billing.data?.data);
  const checkout = createStartBillingCheckout();
  const portal = createOpenBillingPortal();

  // How a round trip to Checkout ended, read once and taken out of the address, so a reload does not repeat it. The
  // local sandbox's Checkout also adds `tax` and `taxId` when it asked for them (for its tests), which go with it.
  const returned = page.url.searchParams.get('checkout');
  const outcome = returned === 'success' || returned === 'cancelled' ? returned : null;
  $effect(() => {
    if (page.url.searchParams.has('checkout')) {
      const url = new URL(page.url.href);
      for (const key of ['checkout', 'tax', 'taxId']) url.searchParams.delete(key);
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
      const response = await checkout.mutateAsync({ data: { plan: target } });
      window.location.assign(response.data.url);
    } catch (error) {
      failure = describeError(error, 'Could not start checkout');
      leaving = null;
    }
  }

  // Change card and Cancel plan both open the payment provider's portal; the intent says which, so the local sandbox's portal,
  // which acts at once, cancels only for Cancel plan.
  async function openPortal(intent: BillingPortalIntent) {
    failure = '';
    leaving = intent;
    try {
      const response = await portal.mutateAsync({ params: { intent } });
      window.location.assign(response.data.url);
    } catch (error) {
      failure = describeError(error, 'Could not open the payment page');
      leaving = null;
    }
  }

  // A switch to another plan: the dialog prices it when it opens and makes it on Confirm. The name stays on the page afterwards,
  // because the provider's confirmation reaches the plan list a moment later.
  let switching = $state<BillingPlan | null>(null);
  let switchOpen = $state(false);
  let switched = $state<string | null>(null);

  const standing = $derived(overview?.state ?? null);
  const subscribed = $derived(standing === 'Active' || standing === 'PastDue');
  const choosable = $derived(mayChoosePlan(standing));
  // A subscription set to end is renewed on the payment page first; the API refuses to switch it.
  const switchable = $derived(standing === 'Active' && Boolean(overview?.canManage) && !overview?.cancelsAtPeriodEnd);
  // The card and the invoices exist once the provider has a customer: the portal can open.
  const hasCustomer = $derived(Boolean(overview?.available && overview.canManage));
  const paymentMethod = createGetBillingPaymentMethod(() => ({ query: { enabled: hasCustomer, refetchOnMount: 'always' } }));
  const month = $derived(overview?.thisMonth ?? null);
  // Without a payment provider an Owner who sees AI usage gets only the AI use section: nothing shown unavailable (A3).
  const ownerParts = $derived(owner && !(usage && overview && !overview.available));
  // The Owner's charge for AI use this month, from billing.
  const charge = $derived(ownerParts && overview?.available ? overview.aiUsage : null);

  // The limits and whose key runs the AI check come with the organization's verification settings.
  const settings = createGetOrganizationVerificationSettings(() => ({ query: { enabled: usage } }));
  const detail = $derived(settings.data?.data);
  const includedKey = $derived(detail?.modelSource !== 'OwnKey');
  const keyText = $derived(
    detail?.modelSource === 'OwnKey'
      ? `Your own key${detail.provider ? `, ${modelProviderLabels[detail.provider]}` : ''}${detail.model ? `, ${detail.model}` : ''}. Your AI provider bills you.`
      : "ScreenHire's key. You're billed for what the checks use.",
  );

  // A link to `#ai-use` (an alert, the Activity log, the Overview) arrives before the panels above the section have loaded, so
  // the browser's one jump lands short. Once billing's queries have settled, bring the section to the top, once.
  const billingFetching = useIsFetching({ queryKey: getGetBillingQueryKey() });
  let arrived = false;
  $effect(() => {
    if (arrived || !usage || page.url.hash !== '#ai-use' || billingFetching.current > 0) return;
    arrived = true;
    void tick().then(() => document.getElementById('ai-use')?.scrollIntoView({ block: 'start' }));
  });
</script>

<svelte:head>
  <title>Billing · ScreenHire</title>
</svelte:head>

<TopBar crumbs={settingsCrumbs('/settings/billing')} />

<main class="flex flex-col">
  <HeaderBand title="Billing" oneRow>
    {#snippet status()}
      {#if overview?.available && standing}<StatusBadge {...billingStatus[standing]} />{/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full min-w-0 flex-col gap-5">
      {#if ownerParts}
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
            <!-- The next payment is This month so far's next invoice, said once there (W7). -->
            {#if overview.cancelsAtPeriodEnd && overview.nextPaymentAt}
              <SummaryItem label="Ends on"><DateText at={overview.nextPaymentAt} /></SummaryItem>
            {/if}
            <SummaryItem label="Open jobs">{openJobsText(overview)}</SummaryItem>
          </SummaryList>
        </Panel>

        <!-- What the organization pays this month, each charge on its line and added up, then the next invoice (docs/architecture/billing.md,
             "The Billing page"). The AI line is the one amount the AI use section below shows too, and links to it. -->
        {#if month}
          <Panel title="This month so far">
            <div class="flex max-w-form flex-col gap-3">
              <ul class="flex flex-col" aria-label="Charges this month">
                {#if month.planAmount !== null}
                  <li class="flex items-baseline justify-between gap-4 py-1.5">
                    <span>{month.planName ?? 'Plan'}{standing === 'Trialing' ? ', free trial' : ''}</span>
                    <span class="tabular-nums">{formatMoney(month.planAmount, month.currency)}</span>
                  </li>
                {/if}
                {#if month.aiAmount !== null}
                  <li class="flex items-baseline justify-between gap-4 py-1.5">
                    <span>
                      AI use,
                      <a href="#ai-use" class="inline-flex min-h-6 items-center text-foreground underline underline-offset-2 pointer-coarse:min-h-11"
                        >{aiCallsText(month.aiCalls)}</a
                      >
                    </span>
                    <!-- As the AI usage panel shows it, so a month of use under a cent reads "Under $0.01" in both. -->
                    <span class="tabular-nums">{formatCost(month.aiAmount)}</span>
                  </li>
                {/if}
                {#if Math.round(month.trialCredit * 100) > 0}
                  <li class="flex items-baseline justify-between gap-4 py-1.5">
                    <span>Included in your trial</span>
                    <span class="tabular-nums">{formatMoney(-month.trialCredit, month.currency)}</span>
                  </li>
                {/if}
                <li class="mt-1 flex items-baseline justify-between gap-4 border-t border-border-subtle pt-2.5 font-semibold">
                  <span>Total so far</span>
                  <span class="tabular-nums" data-total>{formatMoney(month.total, month.currency)}</span>
                </li>
              </ul>
              {#if month.nextInvoiceAmount !== null && month.nextInvoiceAt}
                <p class="text-muted-foreground" data-next-invoice>
                  Next invoice about {formatMoney(month.nextInvoiceAmount, month.currency)}{overview.taxAtCheckout ? ' plus tax where it applies' : ''} on
                  <DateText at={month.nextInvoiceAt} />.
                </p>
              {:else if standing === 'Trialing'}
                <p class="text-muted-foreground">Nothing is charged until you choose a plan.</p>
              {/if}
            </div>
          </Panel>
        {/if}

        {#if hasCustomer}
          <Panel title="Payment method">
            <div class="flex flex-wrap items-center gap-x-4 gap-y-3">
              {#if paymentMethod.isError && !paymentMethod.data}
                <div class="min-w-0 flex-[1_1_12rem]">
                  <QueryError message={describeError(paymentMethod.error, 'Could not load the payment method')} onretry={() => paymentMethod.refetch()} inset />
                </div>
              {:else}
                <div class="flex min-w-0 flex-[1_1_12rem] items-center gap-2">
                  <CreditCardIcon aria-hidden="true" class="shrink-0 text-muted-foreground" />
                  {#if paymentMethod.data}
                    <span data-payment-method>{paymentMethod.data.data.method ? paymentMethodText(paymentMethod.data.data.method) : 'No payment method on file'}</span>
                  {:else}
                    <Skeleton class="h-5 w-40" /><span class="sr-only" role="status">Loading the payment method…</span>
                  {/if}
                </div>
              {/if}
              <div class="flex flex-wrap gap-3">
                <Button variant="outline" size="lg" disabled={leaving !== null} onclick={() => openPortal('ChangeCard')}>Change card</Button>
                {#if subscribed && !overview.cancelsAtPeriodEnd}
                  <Button variant="outline" size="lg" disabled={leaving !== null} onclick={() => openPortal('Cancel')}>Cancel plan</Button>
                {/if}
              </div>
            </div>
          </Panel>

          <BillingInvoices />
        {/if}

        <Panel
          title="Plans"
          footer={switched && overview.currentPlan?.name !== switched ? switchedFooter : standing === 'Trialing' ? trialFooter : undefined}>
          <ul class="-my-3">
            {#each overview.plans as plan (plan.key)}
              <li class="flex flex-wrap items-center gap-x-4 gap-y-2 border-b border-border-subtle py-3 last:border-0">
                <div class="flex min-w-0 flex-[1_1_12rem] flex-col gap-0.5">
                  <p class="font-semibold">{plan.name}</p>
                  <p class="text-muted-foreground">{priceLine(plan, overview.taxAtCheckout)}</p>
                  <p class="text-muted-foreground">{planLimitText(plan)}</p>
                </div>
                <!-- Checkout starts a subscription; a live one is switched through the change dialog, which shows the cost first. -->
                {#if switchable && plan.canSubscribe && plan.key !== overview.currentPlan?.key}
                  <Button
                    variant="outline"
                    size="lg"
                    disabled={leaving !== null}
                    onclick={() => {
                      switching = plan;
                      switchOpen = true;
                    }}
                  >
                    Switch{' '}<span class="sr-only">to {plan.name}</span><ArrowRightIcon aria-hidden="true" />
                  </Button>
                {:else if choosable && plan.canSubscribe}
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
      {/if}

      <!-- The AI use section (docs/architecture/billing.md, "AI use"), at `/settings/billing#ai-use`: the form column, then
           what the checks used as a report at the full width (tables never stop short, S6), then the limits in the column. -->
      {#if usage}
        <div id="ai-use" class="flex w-full min-w-0 scroll-mt-20 flex-col gap-5">
          <div class="flex w-full max-w-form flex-col gap-5">
            {#if settings.isError && !detail}
              <Panel title="AI use">
                <QueryError message={describeError(settings.error, 'Could not load the AI settings')} onretry={() => settings.refetch()} inset />
              </Panel>
            {:else if !detail}
              <Panel title="AI use">
                <div aria-hidden="true" class="flex flex-col gap-3"><Skeleton class="h-5 w-full" /><Skeleton class="h-5 w-full" /></div>
                <p class="sr-only" role="status">Loading AI use…</p>
              </Panel>
            {:else}
              <!-- What ScreenHire's key costs this month (the Owner's charge, from billing) and whose key runs the check, for everyone here. -->
              <Panel title="AI use" footer={aiFooter}>
                <div class="flex flex-col gap-4">
                  <SummaryList>
                    {#if charge}
                      <SummaryItem label="This month"><span class="tabular-nums">{aiMonthText(charge)}</span></SummaryItem>
                    {/if}
                    <SummaryItem label="Key">{keyText}</SummaryItem>
                  </SummaryList>
                  {#if charge}
                    {#each aiUsageNotes(charge, standing) as note (note)}
                      <p class="max-w-measure text-muted-foreground">{note}</p>
                    {/each}
                  {/if}
                </div>
              </Panel>
            {/if}
          </div>

          <AiUsageFigures />

          {#if detail?.limits}
            <!-- As they were on Verification. -->
            <div class="flex w-full max-w-form flex-col gap-5">
              <Panel title="Spending limits">
                {#if owner}
                  <AiUsageLimitsForm limits={detail.limits} />
                {:else}
                  <div class="flex flex-col gap-4">
                    <LimitsSummary limits={detail.limits} />
                    <p class="max-w-measure text-muted-foreground">An owner sets the budget and cap. They apply whichever key runs the checks.</p>
                  </div>
                {/if}
              </Panel>
            </div>
          {/if}
        </div>
      {/if}
    </div>
  </SettingsMenu>
</main>

{#snippet returnedFooter()}
  <p role="status">
    {outcome === 'success' ? 'Checkout finished. This page updates when the payment is confirmed.' : 'Checkout was cancelled. You have not been charged.'}
  </p>
{/snippet}

{#snippet aiFooter()}
  <div class="flex flex-col gap-1 text-muted-foreground">
    {#if includedKey}<p>Bring your own key and this charge goes away.</p>{/if}
    <div class="flex flex-wrap gap-x-4">
      {#if editsModel}
        <a href={aiModelAddress} class="inline-flex min-h-6 items-center text-foreground underline underline-offset-2 pointer-coarse:min-h-11"
          >{includedKey ? 'Use your own key' : 'Change the key'}</a
        >
      {/if}
      <a href="/verification/activity" class="inline-flex min-h-6 items-center text-foreground underline underline-offset-2 pointer-coarse:min-h-11">See every call</a>
    </div>
  </div>
{/snippet}

{#snippet trialFooter()}
  <!-- The trial is a stored date with no card (docs/architecture/billing.md, "The trial"). What AI use above its allowance costs is the AI usage panel's to say. -->
  <p class="text-muted-foreground">The trial needs no card. When it ends, open jobs stay open and a new job needs a plan.</p>
{/snippet}

{#snippet switchedFooter()}
  <p role="status">Your plan is changing to {switched}. This page updates when the payment is confirmed.</p>
{/snippet}

<ChangePlanDialog plan={switching} bind:open={switchOpen} onchanged={(plan) => (switched = plan.name)} />
