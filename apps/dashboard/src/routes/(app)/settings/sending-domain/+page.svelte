<script lang="ts">
  import PlusIcon from '~icons/lucide/plus';
  import RefreshCwIcon from '~icons/lucide/refresh-cw';
  import Trash2Icon from '~icons/lucide/trash-2';
  import type { HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createCheckOrganizationSendingDomain,
    createDeleteOrganizationSendingDomain,
    createGetOrganizationSendingDomain,
    getGetOrganizationSendingDomainHistoryQueryOptions,
    createSetOrganizationSendingDomain,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import CodeBlock from '#lib/components/code-block.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StepLine from '#lib/components/step-line.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { domainStatus, recordName, recordStatus } from '#lib/sending-domain.js';

  // Settings > Email domain (docs/architecture/email.md), on the settings template: the company's own domain that
  // candidate email comes from. ScreenHire lists the DNS records the email provider needs, looks each one up on request, and
  // switches the From address only once the provider has confirmed the domain, so nothing goes out unauthenticated in the
  // company's name. Removing the domain goes back to ScreenHire's address; it confirms (A2). Who added, verified or removed it
  // is the History beside the title (T6).
  const path = '/v1/organization/sending-domain';
  const queryClient = useQueryClient();
  const settled = () => invalidatePath(queryClient, path);

  const sendingDomain = createGetOrganizationSendingDomain();
  const current = $derived(sendingDomain.data?.data);
  const domain = $derived(current?.domain ?? undefined);
  const set = createSetOrganizationSendingDomain(() => ({ mutation: { onSettled: settled } }));
  const check = createCheckOrganizationSendingDomain(() => ({ mutation: { onSettled: settled } }));
  const remove = createDeleteOrganizationSendingDomain(() => ({ mutation: { onSettled: settled } }));

  const schema = z.object({ domain: z.string().trim().min(1, 'Enter your domain, such as example.com.') });
  const { form, errors, enhance, submitting } = superForm(defaults({ domain: '' }, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      announcement = '';
      if (!submitted.valid) return;
      try {
        await set.mutateAsync({ data: { domain: submitted.data.domain } });
        announcement = 'The domain is added. Add its DNS records, then check them.';
      } catch (error) {
        submitted.valid = false;
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.errors._errors = [describeError(error, 'Could not add the domain')];
        }
      }
    },
  });

  let announcement = $state('');
  let checkFailure = $state('');
  async function checkRecords() {
    checkFailure = '';
    try {
      const response = await check.mutateAsync();
      announcement = response.data.status === 'Verified' ? 'The domain is verified.' : 'Checked. Some records are not in place yet.';
    } catch (error) {
      checkFailure = describeError(error, 'Could not check the records');
    }
  }

  // Removing confirms in a dialog that the More menu opens once it has closed.
  let removeOpen = $state(false);
  let chosen: 'remove' | null = null;
  const onMenuClosed = () => {
    if (chosen === 'remove') removeOpen = true;
    chosen = null;
  };

  async function removeDomain() {
    await remove.mutateAsync();
    announcement = 'The email domain is removed. Mail comes from ScreenHire again.';
  }

  const problem = $derived(domain && domain.status !== 'Verified' ? domain.providerMessage : undefined);

  const setupSteps = [
    'Enter the domain your company uses for email.',
    'Add the DNS records we give you where your domain is managed, or send them to whoever manages it.',
    'Press Check records. Changes can take a few hours to show.',
    "Once they pass, email comes from hiring@ your domain. Until then it comes from ScreenHire's address, so nothing stops working.",
  ];
</script>

<svelte:head>
  <title>Email domain · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Email domain' }]} />

<main class="flex flex-col">
  <HeaderBand title="Email domain">
    {#snippet status()}
      {#if domain}<StatusBadge {...domainStatus(domain)} />{/if}
    {/snippet}
    {#snippet actions()}
      {#if domain}
        {#if current?.lastUpdated}
          <HistoryFact value={current.lastUpdated} name="Email domain" page={(before) => getGetOrganizationSendingDomainHistoryQueryOptions({ before })} />
        {/if}
        <Button variant="outline" disabled={check.isPending} onclick={checkRecords}><RefreshCwIcon aria-hidden="true" />Check records</Button>
        <MoreMenu label="More actions" onclosed={onMenuClosed}>
          <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = 'remove')}><Trash2Icon aria-hidden="true" />Remove email domain</DropdownMenu.Item>
        </MoreMenu>
      {/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      <p role="status" class="sr-only">{announcement}</p>
      {#if sendingDomain.isError && !current}
        <Panel title="Domain">
          <QueryError message={describeError(sendingDomain.error, 'Could not load the email domain')} onretry={() => sendingDomain.refetch()} inset />
        </Panel>
      {:else if !current}
        <Panel title="Domain">
          <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-6 w-full max-w-96" /><Skeleton class="h-10 w-40" /></div>
          <p class="sr-only" role="status">Loading the email domain…</p>
        </Panel>
      {:else if !current.available}
        <Panel title="Domain">
          <Empty.Root>
            <Empty.Header>
              <Empty.Title>Sending from your own domain is not set up here</Empty.Title>
              <Empty.Description>It needs an email provider where ScreenHire is hosted. It cannot be set up from this page.</Empty.Description>
            </Empty.Header>
          </Empty.Root>
        </Panel>
      {:else if !domain}
        <!-- This one starts work outside ScreenHire, so it says what happens before anyone types (W9). -->
        <Panel title="How it works">
          <div class="flex flex-col gap-4">
            <p class="max-w-measure">Email to candidates comes from your own domain instead of ScreenHire's address, so candidates know who wrote to them.</p>
            <StepLine items={setupSteps} label="Steps to set up your domain" key={(step) => step}>
              {#snippet children(step)}<p class="max-w-measure">{step}</p>{/snippet}
            </StepLine>
          </div>
        </Panel>
        <form method="POST" use:enhance novalidate aria-label="Add a domain">
          <Panel title="Your domain">
            <div class="flex flex-col gap-5">
              <Field.Field data-invalid={$errors.domain ? true : undefined}>
                <Field.Label for="sending-domain">Domain</Field.Label>
                <Input
                  id="sending-domain"
                  name="domain"
                  type="text"
                  autocomplete="off"
                  placeholder="example.com"
                  aria-describedby="sending-domain-hint"
                  bind:value={$form.domain}
                />
                <Field.Description id="sending-domain-hint">The domain your company's email uses, with no www or https.</Field.Description>
                <Field.Error id="sending-domain-error" errors={$errors.domain?.map((message) => ({ message }))} />
              </Field.Field>
            </div>
            {#snippet footer()}
              <div class="flex flex-wrap items-center justify-end gap-2">
                <div class="mr-auto min-w-0"><FailureText message={$errors._errors?.[0] ?? ''} /></div>
                <Button type="submit" size="lg" disabled={$submitting}><PlusIcon aria-hidden="true" />Add domain</Button>
              </div>
            {/snippet}
          </Panel>
        </form>
      {:else}
        {#if problem}
          <!-- A problem to fix (E2), at the top of the work area: what the provider said and what fixes it. -->
          <Notice>The email provider says: {problem} Check the DNS records below.</Notice>
        {/if}
        {#if checkFailure}<Notice>{checkFailure}</Notice>{/if}

        <Panel title="Domain">
          <SummaryList>
            <SummaryItem label="Domain"><TruncatedText text={domain.domain} /></SummaryItem>
            <SummaryItem label="Email comes from"><TruncatedText text={domain.status === 'Verified' ? domain.fromAddress : 'ScreenHire, until the domain is verified'} /></SummaryItem>
            {#if domain.lastCheckedAt}<SummaryItem label="Checked"><RelativeTime at={domain.lastCheckedAt} /></SummaryItem>{/if}
          </SummaryList>
        </Panel>

        <Panel title="DNS records" count={domain.records.length}>
          <div class="flex flex-col gap-5">
            {#if domain.status !== 'Verified'}
              <p class="max-w-measure">
                Add these records where your domain is managed, or send them to whoever manages it. Then press Check records. Changes can take a few hours to show.
              </p>
            {/if}
            <ul class="flex flex-col divide-y divide-border">
              {#each domain.records as record (record.type + record.host)}
                {@const found = recordStatus(record.state)}
                {@const name = recordName(domain.records, record)}
                <li class="flex flex-col gap-3 py-4 first:pt-0 last:pb-0">
                  <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
                    <h3 class="text-body font-semibold">{name}</h3>
                    <StatusBadge {...found} />
                    <span class="font-mono text-code text-muted-foreground">{record.type}</span>
                    {#if record.priority != null}<span class="text-muted-foreground">priority {record.priority}</span>{/if}
                  </div>
                  <p class="max-w-measure text-muted-foreground">{record.reason}</p>
                  <div class="flex flex-col gap-1">
                    <span class="text-caption text-muted-foreground">Host</span>
                    <CodeBlock code={record.host} label={`${name} host`} />
                  </div>
                  <div class="flex flex-col gap-1">
                    <span class="text-caption text-muted-foreground">Value</span>
                    <CodeBlock code={record.value} label={`${name} value`} />
                  </div>
                </li>
              {/each}
            </ul>
          </div>
        </Panel>
      {/if}
    </div>
  </SettingsMenu>
</main>

<ConfirmDialog
  bind:open={removeOpen}
  title="Remove the email domain?"
  description="Email to candidates comes from ScreenHire's address again, and replies still reach ScreenHire. You can add the domain again later."
  cancelLabel="Keep it"
  confirmLabel="Remove domain"
  failure="Could not remove the domain"
  onconfirm={removeDomain}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>
