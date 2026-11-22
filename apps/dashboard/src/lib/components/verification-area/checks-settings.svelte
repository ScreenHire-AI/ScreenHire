<script lang="ts">
  import { untrack, type Snippet } from 'svelte';
  import SaveIcon from '~icons/lucide/save';
  import type { CheckGroup, HttpValidationProblemDetails, OrganizationVerificationSettings } from '@screenhire/api-client';
  import { ApiError } from '@screenhire/api-client/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import InfoButton from '#lib/components/info-button.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
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

  // The Checks tab (verification-engine.md 10.11): every switch that decides what runs, one section per group of checks, with
  // what the group looks at, an example of what it catches, what turning it off costs and how often it found something this
  // month, and the settings that tune that group inside it: the AI-use policy and the consistency check in Answers, trusted
  // networks in Network, trusted email domains and the device check in Duplicates. The Verification suggested alert's
  // organization default is in Settings > Notification defaults, and each person's own choice in Your account > Notifications.
  // Whoever can see verification reads it all as text; Edit settings gets the controls and one Save (A3). A group that is off
  // records that it did not run, so nobody reads a missing check as clean. The Signal Network has no control: it stays off
  // until legal review, and a switch that cannot be turned on would be a disabled control.
  let {
    detail,
    canEdit,
    found,
  }: {
    detail: OrganizationVerificationSettings;
    canEdit: boolean;
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

  const aiUses = Object.keys(aiUseLabels) as (keyof typeof aiUseLabels)[];
  // The label is "Name: explanation". The name fits a phone-width select; the explanation goes in the info button.
  const aiUseHint = (policy: (typeof aiUses)[number]) => {
    const explanation = aiUseLabels[policy].split(': ')[1] ?? '';
    return `${explanation.charAt(0).toUpperCase()}${explanation.slice(1)}.`;
  };
  const deviceChoices = Object.keys(deviceCheckLabels) as DeviceCheckChoice[];
  const deviceChoiceOf = (): DeviceCheckChoice => detail.deviceCheck ?? 'ByRegion';

  const schema = z.object({
    checks: z.object({ network: z.boolean(), contact: z.boolean(), documents: z.boolean(), answers: z.boolean(), duplicates: z.boolean(), formUse: z.boolean() }),
    llmConsistencyCheck: z.boolean(),
    defaultAiUse: z.enum(['Allowed', 'AllowedWithDisclosure', 'NotAllowed']),
    deviceCheck: z.enum(['ByRegion', 'Notice', 'Consent', 'Off']),
    trustedDomains: z.array(z.string()),
    trustedNetworks: z.array(z.string()),
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
          // The alerts and the AI model are saved on their own pages: they are laid over as saved, never reset from here.
          await save(detail, {
            checks: data.checks,
            llmConsistencyCheck: data.llmConsistencyCheck,
            defaultAiUse: data.defaultAiUse,
            deviceCheck: data.deviceCheck === 'ByRegion' ? null : data.deviceCheck,
            trustedDomains: data.trustedDomains,
            trustedNetworks: data.trustedNetworks,
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
            // A refusal that names no field still says why, under Save.
            if (!domainServerError && !networkServerError && Object.keys(rest).length === 0) {
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

  const foundLine = (group: CheckGroup) => {
    if (!found || found.checked === 0) return null;
    const count = found.applications[group] ?? 0;
    return `Found something in ${count} of ${found.checked} ${found.checked === 1 ? 'application' : 'applications'} this month`;
  };
</script>

<!-- What a group is: the three plain lines, then this month's count. Shared by the form and the read-only view. -->
{#snippet about(group: CheckGroup)}
  {@const copy = checkGroupCopy[group]}
  <div class="flex max-w-measure flex-col gap-1.5">
    <p>{copy.looksAt}</p>
    <p class="text-muted-foreground"><span class="font-medium text-foreground">For example:</span> {copy.example}</p>
    <p class="text-muted-foreground"><span class="font-medium text-foreground">Off:</span> {copy.offCost}</p>
    {#if foundLine(group)}
      <p class="flex items-center gap-1 text-muted-foreground tabular-nums" data-found>
        {foundLine(group)}
        <InfoButton
          label={`About this count for ${checkGroupLabels[group]}`}
          text="Applications this month in which this group counted something towards the level. A count of applications, never a score."
        />
      </p>
    {/if}
  </div>
{/snippet}

<!-- A group's section: its name and switch (or On or Off as text), what it is, and the settings that tune it beside that. -->
{#snippet section(group: CheckGroup, settings?: Snippet)}
  {@const key = keys[group]}
  <section aria-labelledby={`group-${key}`} data-check-group={group} class="grid gap-x-8 gap-y-4 border-t border-border-subtle px-4.5 py-5 first:border-t-0 max-md:px-4 @5xl:grid-cols-2">
    <div class="flex flex-col gap-3">
      <div class="flex items-center justify-between gap-4">
        <h3 id={`group-${key}`} class="text-body font-semibold">
          {#if canEdit}<label for={`check-${key}`}>{checkGroupLabels[group]}</label>{:else}{checkGroupLabels[group]}{/if}
        </h3>
        {#if canEdit}
          <Switch id={`check-${key}`} bind:checked={$form.checks[key]} aria-describedby={`group-${key}-about`} />
        {:else if runs(group)}
          <StatusBadge status="done" label="On" quiet />
        {:else}
          <StatusBadge status="notInEffect" label="Off" />
        {/if}
      </div>
      <div id={`group-${key}-about`}>{@render about(group)}</div>
    </div>
    {#if settings}
      <div class="flex flex-col gap-5">{@render settings()}</div>
    {/if}
  </section>
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
    <div class="flex items-start gap-4">
      <div class="flex min-w-0 flex-1 items-center gap-1">
        <Field.Label for="consistency-check">Compare each application's sources</Field.Label>
        <InfoButton
          label="About comparing each application's sources"
          text={`The consistency check. It sends the resume, the answers and any uploaded LinkedIn export to ${detail.modelSource === 'OwnKey' ? 'your AI provider' : "ScreenHire's AI provider"}, and finds details that contradict each other. Whose key runs it is in Settings, AI model.`}
        />
      </div>
      <Switch id="consistency-check" bind:checked={$form.llmConsistencyCheck} class="mt-1" />
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

{#snippet sections()}
  <p class="px-4.5 pt-4 text-muted-foreground max-md:px-4">A group that is off records that it did not run, so nobody reads a missing check as clean.</p>
  <div class="@container flex flex-col">
    {#each groups as group (group)}
      {@render section(group, group === 'Answers' ? answersSettings : group === 'Network' ? networkSettings : group === 'Duplicates' ? duplicatesSettings : undefined)}
    {/each}
  </div>
{/snippet}

{#if canEdit}
  <form method="POST" use:enhance novalidate aria-label="Checks settings" class="flex flex-col gap-5">
    <Panel title="Checks" flush>
      {@render sections()}
    </Panel>
    <!-- One Save for the whole tab, held at the bottom of the window so a switch near the top never needs a long scroll to keep. -->
    <div class="sticky bottom-0 z-10 flex flex-wrap items-center justify-end gap-2 rounded-panel border border-border bg-card px-4.5 py-3 shadow-surface max-md:px-4" data-save-bar>
      <FailureText message={$errors._errors?.[0]} class="mr-auto" />
      <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
      <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
    </div>
  </form>
{:else}
  <Panel title="Checks" flush>
    {@render sections()}
  </Panel>
{/if}
