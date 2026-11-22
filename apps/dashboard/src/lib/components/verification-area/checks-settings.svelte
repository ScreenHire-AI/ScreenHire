<script lang="ts">
  import { untrack, type Snippet } from 'svelte';
  import { cubicInOut } from 'svelte/easing';
  import { prefersReducedMotion } from 'svelte/motion';
  import { slide } from 'svelte/transition';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import SaveIcon from '~icons/lucide/save';
  import type { CheckGroup, HttpValidationProblemDetails, OrganizationVerificationSettings } from '@screenhire/api-client';
  import { ApiError } from '@screenhire/api-client/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import AlertGrid, { type AlertRow } from '#lib/components/alert-grid.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import InfoButton from '#lib/components/info-button.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import TrustedList from '#lib/components/verification-area/trusted-list.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import {
    checkGroupCopy,
    checkGroupLabels,
    deviceCheckLabels,
    domainProblem,
    networkProblem,
    normalizeDomain,
    type DeviceCheckChoice,
  } from '#lib/verification-settings.js';
  import { createVerificationSettingsSave, savedMessage } from '#lib/verification-settings-save.js';
  import { aiUseLabels, aiUseName } from '#lib/workflow.js';

  // The Checks tab (verification-engine.md 10.11): every switch that decides what runs, one compact row per group so all six fit
  // on one screen (the owner's walk-through, 2026-11-22). A row has the group's switch beside its name, a one-line summary and how
  // often it found something this month; it opens in place to what the group looks at, an example of what it catches, what
  // turning it off costs, and the settings that tune that group: the AI-use policy and the consistency check in Answers, trusted
  // networks in Network, trusted email domains and the device check in Duplicates. Then the Verification suggested alert.
  // Whoever can see verification reads it all as text; Edit settings gets the controls and one Save (A3), which sends every
  // setting whether its row is open or not. A group that is off records that it did not run, so nobody reads a missing check as
  // clean. The Signal Network has no control: it stays off until legal review, and a switch that cannot be turned on would be a
  // disabled control.
  let {
    detail,
    canEdit,
    found,
    opened,
  }: {
    detail: OrganizationVerificationSettings;
    canEdit: boolean;
    /** The group whose row starts open, by its key ("answers"), such as from a link to the AI-use policy. */
    opened?: string;
    /** This month's applications each group found something in, and how many were checked; null while it loads or for none. */
    found: { applications: Partial<Record<CheckGroup, number>>; checked: number } | null;
  } = $props();

  const groups = Object.keys(checkGroupLabels) as CheckGroup[];
  type CheckKey = 'network' | 'contact' | 'documents' | 'answers' | 'duplicates' | 'formUse';
  const keys: Record<CheckGroup, CheckKey> = {
    Network: 'network',
    Contact: 'contact',
    Documents: 'documents',
    Answers: 'answers',
    Duplicates: 'duplicates',
    FormUse: 'formUse',
  };
  const runs = (group: CheckGroup) => detail.checks[keys[group]] ?? true;

  // Every row starts closed, except one a link asks for.
  const open = $state(Object.fromEntries(groups.map((group) => [group, untrack(() => opened) === keys[group]])) as Record<CheckGroup, boolean>);
  const allOpen = $derived(groups.every((group) => open[group]));
  const setAll = (value: boolean) => groups.forEach((group) => (open[group] = value));
  // A link followed while the tab is already showing changes only the hash, so the row it names opens then too.
  $effect(() => {
    const linked = groups.find((group) => keys[group] === opened);
    if (linked) untrack(() => (open[linked] = true));
  });

  const aiUses = Object.keys(aiUseLabels) as (keyof typeof aiUseLabels)[];
  // The label is "Name: explanation". The name fits a phone-width select; the explanation goes in the info button.
  const aiUseHint = (policy: (typeof aiUses)[number]) => {
    const explanation = aiUseLabels[policy].split(': ')[1] ?? '';
    return `${explanation.charAt(0).toUpperCase()}${explanation.slice(1)}.`;
  };
  const deviceChoices = Object.keys(deviceCheckLabels) as DeviceCheckChoice[];
  const deviceChoiceOf = (): DeviceCheckChoice => detail.deviceCheck ?? 'ByRegion';

  const suggestedRow: AlertRow<'suggestedEmail' | 'suggestedSlack'> = {
    label: 'Verification suggested',
    about: 'Tells the reviewers who see the job when an application reaches Verify. It names the job, never the candidate or why.',
    inApp: 'The candidate joins To verify, and the sidebar counts them.',
    email: 'suggestedEmail',
    slack: 'suggestedSlack',
  };

  const schema = z.object({
    checks: z.object({ network: z.boolean(), contact: z.boolean(), documents: z.boolean(), answers: z.boolean(), duplicates: z.boolean(), formUse: z.boolean() }),
    llmConsistencyCheck: z.boolean(),
    defaultAiUse: z.enum(['Allowed', 'AllowedWithDisclosure', 'NotAllowed']),
    deviceCheck: z.enum(['ByRegion', 'Notice', 'Consent', 'Off']),
    trustedDomains: z.array(z.string()),
    trustedNetworks: z.array(z.string()),
    alerts: z.object({ suggestedEmail: z.boolean(), suggestedSlack: z.boolean() }),
  });

  const save = createVerificationSettingsSave();
  let saved = $state('');
  let domainServerError = $state('');
  let networkServerError = $state('');
  const { form, errors, enhance, submitting } = superForm(
    defaults(
      untrack(() => ({
        checks: {
          network: runs('Network'),
          contact: runs('Contact'),
          documents: runs('Documents'),
          answers: runs('Answers'),
          duplicates: runs('Duplicates'),
          formUse: runs('FormUse'),
        },
        llmConsistencyCheck: detail.llmConsistencyCheck,
        defaultAiUse: detail.defaultAiUse as (typeof aiUses)[number],
        deviceCheck: deviceChoiceOf(),
        trustedDomains: [...detail.trustedDomains],
        trustedNetworks: [...detail.trustedNetworks],
        alerts: { suggestedEmail: detail.alerts.suggestedEmail ?? false, suggestedSlack: detail.alerts.suggestedSlack ?? false },
      })),
      zod4(schema),
    ),
    {
      SPA: true,
      dataType: 'json',
      validationMethod: 'submit-only',
      validators: zod4(schema),
      resetForm: false,
      applyAction: false,
      invalidateAll: false,
      async onUpdate({ form: submitted }) {
        saved = '';
        domainServerError = '';
        networkServerError = '';
        if (!submitted.valid) return;
        const data = submitted.data;
        try {
          // The AI usage alert is saved on the AI use tab: it is laid over as saved, never reset from here.
          await save(detail, {
            checks: data.checks,
            llmConsistencyCheck: data.llmConsistencyCheck,
            defaultAiUse: data.defaultAiUse,
            deviceCheck: data.deviceCheck === 'ByRegion' ? null : data.deviceCheck,
            trustedDomains: data.trustedDomains,
            trustedNetworks: data.trustedNetworks,
            alerts: {
              suggestedEmail: data.alerts.suggestedEmail,
              suggestedSlack: data.alerts.suggestedSlack,
              budgetEmail: detail.alerts.budgetEmail ?? false,
              budgetSlack: detail.alerts.budgetSlack ?? false,
            },
          });
          saved = savedMessage;
        } catch (error) {
          if (error instanceof ApiError && error.status === 400) {
            const problem = error.body as HttpValidationProblemDetails;
            domainServerError = problem?.errors?.trustedDomains?.[0] ?? '';
            networkServerError = problem?.errors?.trustedNetworks?.[0] ?? '';
            const { trustedDomains, trustedNetworks, ...rest } = problem?.errors ?? {};
            void trustedDomains;
            void trustedNetworks;
            applyValidationProblem(submitted, { ...problem, errors: rest });
            // A refused setting shows in its own row, so that row opens.
            if (networkServerError) open.Network = true;
            if (domainServerError || 'deviceCheck' in rest) open.Duplicates = true;
            if ('defaultAiUse' in rest || 'llmConsistencyCheck' in rest) open.Answers = true;
            // Only the trusted lists show a refusal beside the field, so any other refused setting says why under Save,
            // and so does a refusal that names no field.
            const refused = Object.values(rest).flat();
            if (refused.length > 0) {
              submitted.valid = false;
              submitted.errors._errors = [refused[0]];
            } else if (!domainServerError && !networkServerError) {
              submitted.valid = false;
              submitted.errors._errors = [describeError(error, 'Could not save the settings')];
            }
          } else {
            submitted.valid = false;
            submitted.errors._errors = [describeError(error, 'Could not save the settings')];
          }
        }
      },
    },
  );

  // The month's count as the row's chip: a count of applications, never a score. Nothing while it loads, before anything was
  // checked, or for a group that is off, where "Nothing found" would read as clean.
  const foundChip = (group: CheckGroup) => {
    if (!found || found.checked === 0 || !runs(group)) return null;
    const count = found.applications[group] ?? 0;
    // The words a screen reader adds, which the panel's info button says for everyone else.
    return count === 0 ? { text: 'Nothing found', more: ' this month' } : { text: `Found in ${count} of ${found.checked}`, more: ' applications this month' };
  };
</script>

<!-- A group's row (A9, the owner's walk-through, 2026-11-22): the switch (or On or Off as text) at its start beside the group's
     name, so nobody wonders which group a switch belongs to, then the button that opens the row in place: the name, the one-line
     summary, the chevron beside them, and the month's count as a quiet chip at the end. The switch sits outside the button,
     because a control inside a button is not reachable on its own. Open, the row shows what the group looks at, its example,
     what off costs and the settings that tune it. -->
{#snippet row(group: CheckGroup, settings?: Snippet)}
  {@const key = keys[group]}
  {@const copy = checkGroupCopy[group]}
  {@const chip = foundChip(group)}
  <div data-check-group={group} class="border-t border-border-subtle first:border-t-0">
    <!-- The switch lines up with the name's line, which is the row's only line at desktop width and its first on a phone. -->
    <div class="flex items-start gap-3 px-4.5 max-md:px-4">
      <div class={['flex h-13 shrink-0 items-center max-md:h-9', canEdit ? 'w-8' : 'w-16']}>
        {#if canEdit}
          <Switch id={`check-${key}`} bind:checked={$form.checks[key]} aria-labelledby={`group-${key}-name`} aria-describedby={`group-${key}-summary`} />
        {:else if runs(group)}
          <StatusBadge status="done" label="On" quiet />
        {:else}
          <StatusBadge status="notInEffect" label="Off" />
        {/if}
      </div>
      <h3 class="min-w-0 flex-1">
        <button
          type="button"
          aria-expanded={open[group]}
          aria-controls={open[group] ? `group-${key}-detail` : undefined}
          class="-mx-2 flex min-h-13 w-[calc(100%+1rem)] items-center gap-3 rounded-lg px-2 py-2 text-left hover:bg-secondary focus-visible:outline-2 focus-visible:outline-ring"
          onclick={() => (open[group] = !open[group])}
        >
          <span class="flex min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-1">
            <span class="flex min-w-0 items-center gap-1.5">
              <span class="flex min-w-0 flex-col md:flex-row md:items-baseline md:gap-3">
                <span id={`group-${key}-name`} class="text-body font-semibold">{checkGroupLabels[group]}</span>
                <span id={`group-${key}-summary`} class="text-muted-foreground">{copy.summary}</span>
              </span>
              <ChevronDownIcon
                aria-hidden="true"
                class={['size-4 shrink-0 text-muted-foreground transition-transform duration-180 ease-in-out motion-reduce:transition-none', open[group] && 'rotate-180']}
              />
            </span>
            {#if chip}
              <Badge variant="muted" class="ml-auto tabular-nums" data-found>{chip.text}<span class="sr-only">{chip.more}</span></Badge>
            {/if}
          </span>
        </button>
      </h3>
    </div>
    {#if open[group]}
      <div
        id={`group-${key}-detail`}
        transition:slide={{ duration: prefersReducedMotion.current ? 0 : 180, easing: cubicInOut }}
        class={['grid gap-x-8 gap-y-5 pr-4.5 pb-5 max-md:px-4 @5xl:grid-cols-2', canEdit ? 'md:pl-15.5' : 'md:pl-23.5']}
      >
        <div class="flex max-w-measure flex-col gap-1.5">
          <p>{copy.looksAt}</p>
          <p class="text-muted-foreground"><span class="font-medium text-foreground">For example:</span> {copy.example}</p>
          <p class="text-muted-foreground"><span class="font-medium text-foreground">Off:</span> {copy.offCost}</p>
        </div>
        {#if settings}
          <div class="flex max-w-form flex-col gap-5">{@render settings()}</div>
        {/if}
      </div>
    {/if}
  </div>
{/snippet}

{#snippet answersSettings()}
  {#if canEdit}
    <Field.Field>
      <div class="flex items-center gap-1">
        <Field.Label for="default-ai-use">AI use while applying</Field.Label>
        <InfoButton label="About AI use while applying" text={`${aiUseHint($form.defaultAiUse)} Asked on every application. A job can set its own.`} />
      </div>
      <select id="default-ai-use" name="defaultAiUse" class={selectClass} bind:value={$form.defaultAiUse}>
        <!-- The fallback when neither the organization nor a job sets one (HiringWorkflow AiUsePolicies.Fallback) says so. -->
        {#each aiUses as policy (policy)}<option value={policy}>{aiUseName(policy)}{policy === 'AllowedWithDisclosure' ? ' (default)' : ''}</option>{/each}
      </select>
    </Field.Field>
    <!-- The switch before its label, as each group's switch is before its name, so the two never sit apart. -->
    <div class="flex items-center gap-3">
      <Switch id="consistency-check" bind:checked={$form.llmConsistencyCheck} />
      <div class="flex min-w-0 items-center gap-1">
        <Field.Label for="consistency-check">Compare each application's sources</Field.Label>
        <InfoButton
          label="About comparing each application's sources"
          text={`The consistency check. It sends the resume, the answers and any uploaded LinkedIn export to ${detail.modelSource === 'OwnKey' ? 'your AI provider' : "ScreenHire's AI provider"}, and finds details that contradict each other. Whose key runs it is on the AI use tab.`}
        />
      </div>
    </div>
  {:else}
    <SummaryList>
      <SummaryItem label="AI use while applying">{aiUseName(detail.defaultAiUse)}. {aiUseHint(detail.defaultAiUse)} A job can set its own.</SummaryItem>
      <SummaryItem label="Compare each application's sources">{detail.llmConsistencyCheck ? 'On' : 'Off'}</SummaryItem>
    </SummaryList>
  {/if}
{/snippet}

{#snippet networkSettings()}
  {#if canEdit}
    <TrustedList
      id="trusted-network"
      label="Trusted networks"
      hint="An office or a partner's address, such as 203.0.113.7, raises no network signal. A range works too, such as 203.0.113.0/24."
      placeholder="203.0.113.7"
      noun="networks"
      numeric
      bind:entries={$form.trustedNetworks}
      bind:serverError={networkServerError}
      problem={networkProblem}
      normalize={(value) => value.trim()}
    />
  {:else}
    <SummaryList>{@render readList('Trusted networks', detail.trustedNetworks, true)}</SummaryList>
  {/if}
{/snippet}

{#snippet duplicatesSettings()}
  {#if canEdit}
    <TrustedList
      id="trusted-domain"
      label="Trusted email domains"
      hint="A shared email on a trusted domain, such as a staffing partner's mailbox, does not raise a duplicate."
      placeholder="partner.example.com"
      noun="domains"
      bind:entries={$form.trustedDomains}
      bind:serverError={domainServerError}
      problem={domainProblem}
      normalize={(value) => normalizeDomain(value)!}
    />
    <Field.Field>
      <div class="flex items-center gap-1">
        <Field.Label for="device-check">Device check</Field.Label>
        <InfoButton
          label="About the device check"
          text="Reads a code for the applicant's device, so Duplicates can find one device behind several applications. Where the law needs consent, ScreenHire asks whatever you choose here."
        />
      </div>
      <select id="device-check" name="deviceCheck" class={selectClass} bind:value={$form.deviceCheck} aria-describedby="device-check-hint">
        {#each deviceChoices as choice (choice)}
          <option value={choice}>{choice === 'ByRegion' ? 'By region (default)' : deviceCheckLabels[choice]}</option>
        {/each}
      </select>
      <Field.Description id="device-check-hint">It also affects Form activity: the device is read only while Form activity is on.</Field.Description>
    </Field.Field>
  {:else}
    <SummaryList>
      {@render readList('Trusted email domains', detail.trustedDomains, false)}
      <SummaryItem label="Device check">{deviceCheckLabels[deviceChoiceOf()]}. It also affects Form activity: the device is read only while Form activity is on.</SummaryItem>
    </SummaryList>
  {/if}
{/snippet}

{#snippet readList(label: string, entries: readonly string[], numeric: boolean)}
  <SummaryItem {label} count={entries.length}>
    {#if entries.length > 0}
      <ul class="flex flex-col gap-1" aria-label={label}>
        {#each entries as entry (entry)}<li class={['break-all', numeric && 'tabular-nums']}>{entry}</li>{/each}
      </ul>
    {:else}
      <span class="text-muted-foreground">None</span>
    {/if}
  </SummaryItem>
{/snippet}

<!-- The Checks panel: what an Off group means is one press away beside the title (W12), and Expand all or Collapse all on the
     right (A9). -->
{#snippet checksPanel()}
  <Panel title="Checks" flush>
    {#snippet tools()}
      <InfoButton label="About the checks">
        <p>A group that is off records that it did not run, so nobody reads a missing check as clean.</p>
        <p class="mt-2">Each row counts the applications this month in which the group found something. A count of applications, never a score.</p>
      </InfoButton>
    {/snippet}
    {#snippet actions()}
      <Button type="button" variant="outline" onclick={() => setAll(!allOpen)}>{allOpen ? 'Collapse all' : 'Expand all'}</Button>
    {/snippet}
    <div class="@container flex flex-col">
      {#each groups as group (group)}
        {@render row(group, group === 'Answers' ? answersSettings : group === 'Network' ? networkSettings : group === 'Duplicates' ? duplicatesSettings : undefined)}
      {/each}
    </div>
  </Panel>
{/snippet}

{#if canEdit}
  <form method="POST" use:enhance novalidate aria-label="Checks settings" class="flex flex-col gap-5">
    {@render checksPanel()}
    <Panel title="Notifications" flush>
      <AlertGrid
        rows={[suggestedRow]}
        bind:values={$form.alerts}
        emailAvailable={detail.emailAvailable}
        slackConnected={detail.slackConnected}
        label="Verification alerts"
      />
    </Panel>
    <!-- One Save for the whole tab, held at the bottom of the window so a switch near the top never needs a long scroll to keep. -->
    <div class="sticky bottom-0 z-10 flex flex-wrap items-center justify-end gap-2 rounded-panel border border-border bg-card px-4.5 py-3 shadow-surface max-md:px-4" data-save-bar>
      <FailureText message={$errors._errors?.[0]} class="mr-auto" />
      <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
      <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
    </div>
  </form>
{:else}
  {@render checksPanel()}
{/if}
