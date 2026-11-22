<script lang="ts">
  import CreditCardIcon from '~icons/lucide/credit-card';
  import type { BillingPlan, BillingPortalIntent } from '@screenhire/api-client';
  import {
    createGetAiCredits,
    createGetBilling,
    createGetBillingPaymentMethod,
    createGetOrganizationVerificationSettings,
    createListBillingInvoices,
    createOpenBillingPortal,
    createStartBillingCheckout,
  } from '@screenhire/api-client/svelte-query';
  import { tick } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import AiCreditsPanel from '#lib/components/ai-credits-panel.svelte';
  import AiUsageFigures from '#lib/components/ai-usage-figures.svelte';
  import AutoReloadForm from '#lib/components/auto-reload-form.svelte';
  import BillingInvoices from '#lib/components/billing-invoices.svelte';
  import ChangePlanDialog from '#lib/components/change-plan-dialog.svelte';
  import CreditDeductions from '#lib/components/credit-deductions.svelte';
  import CreditPurchases from '#lib/components/credit-purchases.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Notice from '#lib/components/notice.svelte';
  import OverviewColumns from '#lib/components/overview-columns.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PlanCard from '#lib/components/plan-card.svelte';
  import PlanComparison from '#lib/components/plan-comparison.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import { settingsCrumbs } from '#lib/settings.js';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { aiMonthText, aiUsageNotes, billingStatus, formatCredits, formatMoney, mayChoosePlan, paymentMethodText } from '#lib/billing.js';
  import { can } from '#lib/permissions.js';
  import { describeError } from '#lib/query.js';
  import { aiModelAddress, canEditVerification, canSeeUsage } from '#lib/verification-area.js';
  import { modelProviderLabels } from '#lib/verification-settings.js';

  // Everything the organization pays, in one place (docs/architecture/billing.md). For an Owner (ManageBilling): the plan and open
  // jobs, this month so far added up with the next invoice, the payment method, the invoices and the plans, in the form template
  // (section 7). Then the AI use section, for whoever can see AI usage (ai-usage.md): the prepaid AI credits (ADR-0032), with buying
  // and auto-reload for an Owner, what the AI check used this month and on whose key, what the checks used (a report at the full
  // width), and the purchases and deductions behind the balance. Someone
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

  // How a round trip to Checkout ended, read once and taken out of the address, so a reload does not repeat it: `checkout` for a
  // plan, `credits` for AI credits. The local sandbox's Checkout also adds `tax` and `taxId` when it asked for them (for its
  // tests), which go with it. Coming back grants nothing: the credits arrive when the payment is confirmed.
  const outcomeOf = (value: string | null) => (value === 'success' || value === 'cancelled' ? value : null);
  const outcome = outcomeOf(page.url.searchParams.get('checkout'));
  const creditOutcome = outcomeOf(page.url.searchParams.get('credits'));
  $effect(() => {
    if (page.url.searchParams.has('checkout') || page.url.searchParams.has('credits')) {
      const url = new URL(page.url.href);
      for (const key of ['checkout', 'credits', 'tax', 'taxId']) url.searchParams.delete(key);
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
  // What each plan's card offers: Change to on a live subscription (the change dialog prices it first), Choose while there is
  // none (Checkout). The plan card's own button only brings these into view, so it shows only when one of them will be there.
  function actionFor(plan: BillingPlan) {
    if (switchable && plan.canSubscribe && plan.key !== overview?.currentPlan?.key) {
      return {
        label: `Change to ${plan.name}`,
        run: () => {
          switching = plan;
          switchOpen = true;
        },
      };
    }
    if (choosable && plan.canSubscribe) return { label: `Choose ${plan.name}`, run: () => leave(plan.key) };
    return null;
  }
  const offersChoice = $derived(overview?.plans.some((plan) => actionFor(plan) !== null) ?? false);
  // The plan in force is marked on its card: the subscription's, or the trial's plan while the trial runs (still to be chosen).
  const marked = $derived(subscribed || standing === 'Trialing' ? (overview?.currentPlan?.key ?? null) : null);
  // Change plan and Choose a plan bring the plans into view and move focus there, to the cards that change it.
  function showPlans() {
    const target = document.getElementById('plans');
    target?.scrollIntoView({ block: 'start' });
    target?.focus({ preventScroll: true });
  }
  // The clock the trial's days left are counted from, read once per visit: a page left open past midnight counts from the
  // visit until it is opened again. A live update redraws the card from the new overview, still against this clock.
  const now = Date.now();

  // The card and the invoices exist once the provider has a customer: the portal can open.
  const hasCustomer = $derived(Boolean(overview?.available && overview.canManage));
  const paymentMethod = createGetBillingPaymentMethod(() => ({ query: { enabled: hasCustomer, refetchOnMount: 'always' } }));
  const month = $derived(overview?.thisMonth ?? null);
  // Without a payment provider an Owner who sees AI usage gets only the AI use section: nothing shown unavailable (A3).
  const ownerParts = $derived(owner && !(usage && overview && !overview.available));
  // The Owner's charge for AI use this month, from billing.
  const charge = $derived(ownerParts && overview?.available ? overview.aiUsage : null);

  // Whose key runs the AI check comes with the organization's verification settings.
  const settings = createGetOrganizationVerificationSettings(() => ({ query: { enabled: usage } }));
  const detail = $derived(settings.data?.data);
  const includedKey = $derived(detail?.modelSource !== 'OwnKey');
  const keyText = $derived(
    detail?.modelSource === 'OwnKey'
      ? `Your own key${detail.provider ? `, ${modelProviderLabels[detail.provider]}` : ''}${detail.model ? `, ${detail.model}` : ''}. Your AI provider bills you.`
      : "ScreenHire's key. The checks use your AI credits.",
  );
  // The prepaid AI credits, where a payment provider is set up.
  const creditsQuery = createGetAiCredits(() => ({ query: { enabled: usage, refetchOnMount: 'always' } }));
  const credits = $derived(creditsQuery.data?.data.available ? creditsQuery.data.data : null);

  // A link to `#ai-use` (an alert, the Activity log, the Overview) arrives before the panels above the section have loaded, so
  // the browser's one jump lands short. Once the Owner's queries above it have settled, bring the section to the top, once. The
  // invoices are read here only for their state: the same query as the Invoices panel's, so no second request.
  const invoices = createListBillingInvoices(() => ({ query: { enabled: ownerParts && hasCustomer } }));
  const aboveSettled = $derived(
    !ownerParts || (!billing.isPending && (!hasCustomer || (!paymentMethod.isPending && !invoices.isPending))),
  );
  let arrived = false;
  $effect(() => {
    const target = page.url.hash === '#ai-use' ? 'ai-use' : page.url.hash === '#ai-credits' ? 'ai-credits' : null;
    if (arrived || !usage || !target || !aboveSettled || (target === 'ai-credits' && creditsQuery.isPending)) return;
    arrived = true;
    void tick().then(() => document.getElementById(target)?.scrollIntoView({ block: 'start' }));
  });
</script>

<svelte:head>
  <title>Billing · ScreenHire</title>
</svelte:head>

<TopBar crumbs={settingsCrumbs('/settings/billing')} />

<main class="flex flex-col">
  <HeaderBand title="Billing" frame oneRow>
    {#snippet status()}
      {#if overview?.available && standing}<StatusBadge {...billingStatus[standing]} />{/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <!-- With billing set up, the Owner's page is an overview (design system section 7): the plan and what it costs this month in the
         main column, with the AI use they paid for; the card, the credits and the plans to change to in the side column; then the
         purchases and deductions under both, long tables that never push the side column down on a phone (docs/architecture/billing.md,
         "The Billing page"). Anything else (loading, an error, no payment provider, someone who sees only AI use) is one column. -->
    {#if ownerParts && overview?.available}
      {#if standing === 'PastDue'}
        <Notice>Your last payment failed. Update your payment details to keep publishing.</Notice>
      {:else if standing === 'Ended'}
        <!-- The badge beside the title says Ended, for a subscription or a trial that ran out alike. -->
        <Notice>Open jobs stay open, but you cannot publish new jobs until you choose a plan.</Notice>
      {/if}
      <FailureText message={failure} />
      {#if billing.isError}<FailureText message={describeError(billing.error, 'Could not refresh billing')} />{/if}

      <OverviewColumns within="settings">
        {#snippet main()}
          <PlanCard {overview} {now} onchoose={offersChoice ? showPlans : undefined} footer={outcome ? returnedFooter : undefined} />
          {@render thisMonth()}
          {#if hasCustomer}<BillingInvoices />{/if}
          {#if usage}
            <!-- The AI use section (docs/architecture/billing.md, "AI credits"), at `/settings/billing#ai-use`. -->
            <div id="ai-use" class="flex min-w-0 scroll-mt-20 flex-col gap-5">
              {@render aiUse()}
              <AiUsageFigures />
            </div>
          {/if}
        {/snippet}
        {#snippet side()}
          {#if hasCustomer}{@render paymentMethodPanel()}{/if}
          {#if usage}{@render aiCredits()}{/if}
          {@render plansPanel()}
        {/snippet}
      </OverviewColumns>

      {#if usage}{@render ledger()}{/if}
    {:else}
      {#if ownerParts}
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
        {:else}
          <Panel title="Plan">
            <Empty.Root>
              <Empty.Header>
                <Empty.Title>Billing is not set up here</Empty.Title>
                <Empty.Description>It needs a payment provider where ScreenHire is hosted. Nothing is limited until it is.</Empty.Description>
              </Empty.Header>
            </Empty.Root>
          </Panel>
        {/if}
      {/if}

      <!-- The AI use section on its own: the credits and auto-reload, this month's use, what the checks used, then the purchases and
           deductions behind the balance. -->
      {#if usage}
        <div id="ai-use" class="flex w-full min-w-0 scroll-mt-20 flex-col gap-5">
          {@render aiCredits()}
          {@render aiUse()}
          <AiUsageFigures />
          {@render ledger()}
        </div>
      {/if}
    {/if}
  </SettingsMenu>
</main>

<!-- What the organization pays this month, each charge on its line and added up, then the next invoice (docs/architecture/billing.md,
     "The Billing page"). AI use is prepaid: the credits bought this month are the charge, and link to the credits. -->
{#snippet thisMonth()}
  {#if month && overview?.available}
    <Panel title="This month so far">
      <div class="flex max-w-form flex-col gap-3">
        <ul class="flex flex-col" aria-label="Charges this month">
          {#if month.planAmount !== null}
            <li class="flex items-baseline justify-between gap-4 py-1.5">
              <span>{month.planName ?? 'Plan'}{standing === 'Trialing' ? ', free trial' : ''}</span>
              <span class="tabular-nums">{formatMoney(month.planAmount, month.currency)}</span>
            </li>
          {/if}
          {#if month.creditsBought > 0}
            <li class="flex items-baseline justify-between gap-4 py-1.5">
              <a href="#ai-credits" class="inline-flex min-h-6 items-center text-foreground underline underline-offset-2 pointer-coarse:min-h-11">AI credits</a>
              <span class="tabular-nums">{formatCredits(month.creditsBought)}</span>
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
          <p class="text-muted-foreground">The plan costs nothing until you choose one.</p>
        {/if}
      </div>
    </Panel>
  {/if}
{/snippet}

{#snippet paymentMethodPanel()}
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
        {#if subscribed && !overview?.cancelsAtPeriodEnd}
          <Button variant="outline" size="lg" disabled={leaving !== null} onclick={() => openPortal('Cancel')}>Cancel plan</Button>
        {/if}
      </div>
    </div>
  </Panel>
{/snippet}

{#snippet plansPanel()}
  {#if overview?.available}
    <Panel
      id="plans"
      tabindex={-1}
      class="scroll-mt-20 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
      title="Plans"
      footer={switched && overview.currentPlan?.name !== switched ? switchedFooter : standing === 'Trialing' ? trialFooter : undefined}>
      <PlanComparison
        plans={overview.plans}
        current={marked}
        currentLabel={standing === 'Trialing' ? 'Your trial' : 'Your plan'}
        taxAtCheckout={overview.taxAtCheckout}
        {actionFor}
        disabled={leaving !== null}
      />
    </Panel>
  {/if}
{/snippet}

<!-- The prepaid credits (`#ai-credits`) and, for an Owner on ScreenHire's key, auto-reload. -->
{#snippet aiCredits()}
  {#if creditsQuery.isError && !creditsQuery.data}
    <Panel title="AI credits">
      <QueryError message={describeError(creditsQuery.error, 'Could not load the AI credits')} onretry={() => creditsQuery.refetch()} inset />
    </Panel>
  {:else if credits}
    <AiCreditsPanel {credits} {owner} returned={creditOutcome} />
    {#if owner && !credits.ownKey}
      <Panel title="Auto-reload"><AutoReloadForm {credits} /></Panel>
    {/if}
  {/if}
{/snippet}

<!-- What ScreenHire's key costs this month (the Owner's charge, from billing) and whose key runs the check, for everyone here. -->
{#snippet aiUse()}
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
    <Panel title="AI use" footer={aiFooter}>
      <div class="flex flex-col gap-4">
        <SummaryList>
          {#if charge}
            <SummaryItem label="This month"><span class="tabular-nums">{aiMonthText(charge)}</span></SummaryItem>
          {/if}
          <SummaryItem label="Key">{keyText}</SummaryItem>
        </SummaryList>
        {#if charge}
          {#each aiUsageNotes(charge) as note (note)}
            <p class="max-w-measure text-muted-foreground">{note}</p>
          {/each}
        {/if}
      </div>
    </Panel>
  {/if}
{/snippet}

<!-- Every credit added and every deduction, each traceable to the check that used it. -->
{#snippet ledger()}
  {#if credits}
    {#if owner}<CreditPurchases />{/if}
    <CreditDeductions />
  {/if}
{/snippet}

{#snippet returnedFooter()}
  <p role="status">
    {outcome === 'success' ? 'Checkout finished. This page updates when the payment is confirmed.' : 'Checkout was cancelled. You have not been charged.'}
  </p>
{/snippet}

{#snippet aiFooter()}
  <div class="flex flex-col gap-1 text-muted-foreground">
    {#if includedKey}<p>Bring your own key and the checks use no credits.</p>{/if}

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
