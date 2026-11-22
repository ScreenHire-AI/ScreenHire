<script lang="ts">
  import type { AiCreditsView } from '@screenhire/api-client';
  import { createStartAiCreditCheckout } from '@screenhire/api-client/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { creditExpiryText, formatCredits } from '#lib/billing.js';
  import { describeError } from '#lib/query.js';

  // The organization's prepaid AI credits (ADR-0032, docs/architecture/billing.md "AI credits"): the balance, and for an Owner the
  // fixed amounts to buy. Buying goes to the payment provider's Checkout; the credits arrive when the provider confirms the payment,
  // never because the browser came back, so the return only says the page updates when it is confirmed. At zero on ScreenHire's
  // key the AI check pauses, which the badge says with its icon, colour and words; it is never a mark against a candidate.
  let { credits, owner, returned = null }: { credits: AiCreditsView; owner: boolean; returned?: 'success' | 'cancelled' | null } = $props();

  const checkout = createStartAiCreditCheckout();
  let leaving = $state<number | null>(null);
  let failure = $state('');
  async function buy(amount: number) {
    failure = '';
    leaving = amount;
    try {
      const response = await checkout.mutateAsync({ data: { amount } });
      window.location.assign(response.data.url);
    } catch (error) {
      failure = describeError(error, 'Could not start checkout');
      leaving = null;
    }
  }
</script>

<Panel title="AI credits" id="ai-credits" class="scroll-mt-20" footer={returned ? returnedFooter : undefined}>
  <div class="flex flex-col gap-4">
    <SummaryList>
      <SummaryItem label="Balance">
        <span class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span class="tabular-nums" data-credit-balance>{formatCredits(credits.balance)}</span>
          {#if credits.paused}<StatusBadge status="notInEffect" label="AI checks paused" />{/if}
        </span>
      </SummaryItem>
    </SummaryList>

    {#if credits.ownKey}
      <p class="max-w-measure text-muted-foreground">Your own key runs the AI check, so it uses no credits.</p>
    {:else if credits.paused}
      <p class="max-w-measure text-muted-foreground">
        The AI checks are paused until credits are added. The other checks keep running, and a paused check says nothing about a candidate.
      </p>
    {:else}
      <p class="max-w-measure text-muted-foreground">The AI checks on ScreenHire's key use these credits as they run.</p>
    {/if}

    {#if owner}
      <div class="flex flex-col gap-2">
        <p class="font-semibold" id="ai-credits-buy">Add credits</p>
        <div class="flex flex-wrap gap-3" role="group" aria-labelledby="ai-credits-buy">
          {#each credits.amounts as amount (amount)}
            <Button variant="outline" size="lg" disabled={leaving !== null} onclick={() => buy(amount)}>
              {formatCredits(amount)}{' '}<span class="sr-only">of AI credits</span>
            </Button>
          {/each}
        </div>
        <p class="text-muted-foreground">{creditExpiryText(credits.expiryMonths)}</p>
        <FailureText message={failure} />
      </div>
    {:else}
      <p class="text-muted-foreground">An owner adds credits and turns on auto-reload.</p>
    {/if}
  </div>
</Panel>

{#snippet returnedFooter()}
  <p role="status">
    {returned === 'success' ? 'Payment sent. The credits appear here when the payment is confirmed.' : 'Checkout was cancelled. You have not been charged.'}
  </p>
{/snippet}
