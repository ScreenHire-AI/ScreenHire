<script lang="ts">
  import { untrack } from 'svelte';
  import type { BillingPlan } from '@screenhire/api-client';
  import { createChangeBillingPlan, createPreviewBillingPlanChange } from '@screenhire/api-client/svelte-query';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { formatMoney } from '#lib/billing.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // Confirms a plan change on a live subscription (design system section 9, A2): the cost as plain numbers, "$X now, then $Y a
  // month from DATE", worked out by the payment provider when the dialog opens, then the one primary action. The change is
  // made through the API with the date the price was worked out at, so the charge is the one on screen
  // (docs/architecture/billing.md). A failure stays in the dialog; the confirmation is refused when the price is out of date.
  let {
    plan,
    open = $bindable(false),
    onchanged,
  }: {
    plan: BillingPlan | null;
    open?: boolean;
    /** The change went through. The provider tells the page about it, so the page only says so. */
    onchanged: (plan: BillingPlan) => void;
  } = $props();

  const preview = createPreviewBillingPlanChange();
  const change = createChangeBillingPlan();
  const quote = $derived(preview.data?.data);
  let failure = $state('');

  // Prices the plan each time the dialog opens, so a quote is never older than the click that asked for it.
  $effect(() => {
    if (open && plan) {
      const key = plan.key;
      untrack(() => {
        failure = '';
        change.reset();
        preview.mutate({ data: { plan: key } });
      });
    }
  });

  async function confirm() {
    if (!plan || !quote) return;
    failure = '';
    try {
      await change.mutateAsync({ data: { plan: plan.key, prorationDate: quote.prorationDate } });
      open = false;
      onchanged(plan);
    } catch (error) {
      failure = describeError(error, 'Could not change the plan');
      // Price it again: a refusal for an out-of-date price needs a new one, and a retry with the same date would only get
      // the payment provider's earlier answer back.
      preview.mutate({ data: { plan: plan.key } });
    }
  }

  // Focus the dialog itself, not its first control: that is the date in the price line, whose tooltip would open over the
  // text before anyone asked for it. bits-ui calls this without dispatching the event, so the dialog comes from its ref.
  let content = $state<HTMLElement | null>(null);
  function onOpenAutoFocus(event: Event) {
    event.preventDefault();
    content?.focus();
  }
</script>

<Dialog.Root bind:open>
  <Dialog.Content bind:ref={content} class="sm:max-w-md" {onOpenAutoFocus}>
    <Dialog.Header>
      <Dialog.Title>Change to {plan?.name ?? 'this plan'}?</Dialog.Title>
      <Dialog.Description>The plan changes as soon as you confirm. Your open jobs stay open.</Dialog.Description>
    </Dialog.Header>
    {#if preview.isError}
      <FailureText message={describeError(preview.error, 'Could not work out the cost of the change')} />
    {:else if quote}
      <div class="flex flex-col gap-2">
        <p class="font-semibold">
          {quote.amountDueNow > 0 ? `${formatMoney(quote.amountDueNow, quote.currency)} now` : 'Nothing now'}, then
          {formatMoney(quote.nextPaymentAmount, quote.currency)} a month from <DateText at={quote.nextPaymentAt} />
        </p>
        <p class="text-muted-foreground">
          {quote.amountDueNow > 0
            ? 'The first amount is the difference in price for the rest of this billing period, charged to your card on file.'
            : 'The unused time on your current plan is credited toward your next payment.'}
        </p>
      </div>
    {:else}
      <p role="status" class="text-muted-foreground">Working out the cost…</p>
    {/if}
    <FailureText message={failure} />
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Keep my plan</Dialog.Close>
      <Button size="lg" disabled={!quote || change.isPending} onclick={confirm}>Confirm change</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
