<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import CheckIcon from '~icons/lucide/check';
  import XIcon from '~icons/lucide/x';
  import { decideOAuthAuthorization, type ApiKeyScope } from '@screenhire/api-client';
  import { tick } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { problemText } from '#lib/oauth.js';
  import type { PageProps } from './$types';

  // An assistant asked to connect to this organization (docs/architecture/oauth-server.md). The load asked the API to check
  // the request; this page shows who is asking and what they would get, and sends the person's answer. Read is always on.
  // Write and Verification read start off, so the person opts in to each. The answer comes back as the assistant's own
  // address, carrying a code or a refusal, and the browser goes there with a full page load: it is another site, not a
  // route. The assistant's `state` goes back unchanged and is never shown.
  let { data }: PageProps = $props();

  const info = $derived(data.info);

  // What each scope lets the assistant do, in the words the person decides with.
  const scopeCopy: Record<ApiKeyScope, { title: string; opens: (client: string) => string }> = {
    Read: { title: 'Read', opens: () => 'Read jobs, candidates, applications, reports and saved candidates.' },
    Write: {
      title: 'Write',
      opens: (client) => `Add notes, tags and tasks and move candidates between stages. Everything it does shows in the activity under ${client}.`,
    },
    VerificationRead: {
      title: 'Verification read',
      opens: () => 'Read verification results. Each read is logged. A level is evidence for a person to review. It is never a reason to reject anyone.',
    },
  };
  const order: ApiKeyScope[] = ['Read', 'Write', 'VerificationRead'];

  // Read is offered every time, whether or not the assistant listed it.
  const offers = $derived(
    order.flatMap((scope) => {
      if (scope === 'Read') return [{ scope, grantable: true, reason: null as string | null }];
      const offer = info?.scopes.find((candidate) => candidate.scope === scope);
      return offer ? [offer] : [];
    }),
  );

  let chosen = $state<Record<ApiKeyScope, boolean>>({ Read: true, Write: false, VerificationRead: false });
  let deciding = $state(false);
  let failure = $state('');
  let heading = $state<HTMLHeadingElement>();

  // The Approve and Deny buttons are gone when the request cannot be approved, so focus moves to the new title (WCAG 2.4.3).
  $effect(() => {
    if (!info) {
      void tick().then(() => heading?.focus());
    }
  });

  async function decide(approve: boolean) {
    failure = '';
    deciding = true;
    const granted = approve ? offers.filter((offer) => offer.scope === 'Read' || (offer.grantable && chosen[offer.scope])).map((offer) => offer.scope) : ['Read' as const];
    try {
      // The generated type lists only 200, 401 and 403; the API also answers 400 with a problem whose detail is for the person.
      const response = (await decideOAuthAuthorization({ ...data.params, approve, grantedScopes: granted })) as {
        status: number;
        data?: { redirectUrl?: string; detail?: string | null };
      };
      if (response.status === 200 && response.data?.redirectUrl) {
        // The assistant's own address, which may be on this computer. The buttons stay off: the page is about to go.
        window.location.assign(response.data.redirectUrl);
        return;
      }
      failure = problemText(response.status, response.data?.detail);
    } catch {
      failure = 'We could not reach ScreenHire. Check your connection and try again.';
    }
    deciding = false;
  }
</script>

<svelte:head>
  <title>Connect an assistant · ScreenHire</title>
</svelte:head>

<main class="flex flex-col">
  <HeaderBand form title={info ? `Connect ${info.clientName} to ${info.organizationName}` : "This assistant can't be connected"} bind:heading />

  <!-- The form template (section 7): one 688px column, actions at the end. -->
  <div class="mx-auto flex w-full max-w-form flex-col gap-5 px-4 py-6 md:px-6">
    {#if !info}
      <Panel title="Connection request" footer={home}>
        <FailureText message={data.problem ?? 'We could not check this request. Start the connection again from the assistant.'} />
      </Panel>
    {:else}
      <Panel title="Who is asking">
        <div class="flex flex-col gap-4">
          <p class="break-words">
            <strong class="font-semibold">{info.clientName}</strong> wants to act in <strong class="font-semibold">{info.organizationName}</strong> for
            you. It can use only what you allow below, and you can disconnect it at any time in Settings.
          </p>
          <SummaryList>
            <SummaryItem label="Goes back to">
              <span class="flex flex-wrap items-center gap-2">
                <span class="font-mono text-code break-all">{info.redirectHost}</span>
                {#if info.redirectIsLoopback}<StatusBadge status="review" label="This assistant runs on your computer" />{/if}
              </span>
            </SummaryItem>
          </SummaryList>
          <p class="text-muted-foreground">ScreenHire does not check who runs an assistant. Approve it only if you started this connection.</p>
        </div>
      </Panel>

      <Panel title="What it may do" footer={decision}>
        <fieldset class="flex flex-col gap-4">
          <legend class="sr-only">What {info.clientName} may do</legend>
          {#each offers as offer (offer.scope)}
            {@const copy = scopeCopy[offer.scope]}
            {@const locked = offer.scope === 'Read'}
            <label class="flex items-start gap-3 pointer-coarse:min-h-11">
              <input
                type="checkbox"
                class="mt-0.5 size-4 shrink-0 accent-primary"
                checked={locked ? true : chosen[offer.scope]}
                disabled={locked || !offer.grantable || deciding}
                onchange={(event) => (chosen[offer.scope] = event.currentTarget.checked)}
              />
              <span class="flex min-w-0 flex-col gap-0.5">
                <span class="font-medium">{copy.title}</span>
                <span class="text-muted-foreground">{copy.opens(info.clientName)}</span>
                {#if locked}<span class="text-muted-foreground">Always on.</span>{:else if !offer.grantable && offer.reason}<span class="text-muted-foreground">{offer.reason}</span>{/if}
              </span>
            </label>
          {/each}
        </fieldset>
      </Panel>
    {/if}
  </div>
</main>

{#snippet decision()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <FailureText message={failure} class="mr-auto" />
    <Button variant="outline" size="lg" disabled={deciding} onclick={() => decide(false)}><XIcon aria-hidden="true" />Deny</Button>
    <Button size="lg" disabled={deciding} onclick={() => decide(true)}><CheckIcon aria-hidden="true" />Approve</Button>
  </div>
{/snippet}

{#snippet home()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <Button href="/" size="lg"><ArrowRightIcon aria-hidden="true" />Go to ScreenHire</Button>
  </div>
{/snippet}
