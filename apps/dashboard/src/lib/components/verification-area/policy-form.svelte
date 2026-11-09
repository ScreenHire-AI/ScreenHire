<script lang="ts">
  import { untrack } from 'svelte';
  import PlusIcon from '~icons/lucide/plus';
  import SaveIcon from '~icons/lucide/save';
  import XIcon from '~icons/lucide/x';
  import type { HttpValidationProblemDetails, OrganizationVerificationSettings } from '@screenhire/api-client';
  import { ApiError } from '@screenhire/api-client/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import { deviceCheckLabels, domainProblem, maxTrustedEntries, networkProblem, normalizeDomain, type DeviceCheckChoice } from '#lib/verification-settings.js';
  import { createVerificationSettingsSave, savedMessage } from '#lib/verification-settings-save.js';
  import { aiUseLabels, aiUseName } from '#lib/workflow.js';

  // The Policy tab (verification-engine.md 10.11): the AI-use policy asked on every application, the device check, and the
  // email domains and networks the organization trusts. Whoever can see verification reads them; whoever can edit settings
  // changes them. The Signal Network has no control here: it stays off until legal review, and a switch that cannot be turned on
  // would be a disabled control (A3).
  let { detail, canEdit }: { detail: OrganizationVerificationSettings; canEdit: boolean } = $props();

  const aiUses = Object.keys(aiUseLabels) as (keyof typeof aiUseLabels)[];
  // The label is "Name: explanation". The name fits a phone-width select; the explanation of the chosen policy goes in the hint.
  const aiUseHint = (policy: (typeof aiUses)[number]) => {
    const explanation = aiUseLabels[policy].split(': ')[1] ?? '';
    return `${explanation.charAt(0).toUpperCase()}${explanation.slice(1)}.`;
  };
  const deviceChoices = Object.keys(deviceCheckLabels) as DeviceCheckChoice[];
  const deviceChoiceOf = (): DeviceCheckChoice => detail.deviceCheck ?? 'ByRegion';

  const schema = z.object({
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
          await save(detail, {
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
          } else {
            submitted.valid = false;
            submitted.errors._errors = [describeError(error, 'Could not save the settings')];
          }
        }
      },
    },
  );

  // The trusted lists: an entry is checked as it is typed, the way the server checks it again on save.
  let domainInput = $state('');
  let networkInput = $state('');
  let domainError = $state('');
  let networkError = $state('');

  function addDomain() {
    domainServerError = '';
    const problem = domainProblem(domainInput);
    if (problem) {
      domainError = problem;
      return;
    }
    const domain = normalizeDomain(domainInput)!;
    if ($form.trustedDomains.length >= maxTrustedEntries && !$form.trustedDomains.includes(domain)) {
      domainError = `At most ${maxTrustedEntries} domains.`;
      return;
    }
    domainError = '';
    if (!$form.trustedDomains.includes(domain)) $form.trustedDomains = [...$form.trustedDomains, domain];
    domainInput = '';
  }

  function addNetwork() {
    networkServerError = '';
    const problem = networkProblem(networkInput);
    if (problem) {
      networkError = problem;
      return;
    }
    const network = networkInput.trim();
    if ($form.trustedNetworks.length >= maxTrustedEntries && !$form.trustedNetworks.includes(network)) {
      networkError = `At most ${maxTrustedEntries} networks.`;
      return;
    }
    networkError = '';
    if (!$form.trustedNetworks.includes(network)) $form.trustedNetworks = [...$form.trustedNetworks, network];
    networkInput = '';
  }

  const onEnter = (add: () => void) => (event: KeyboardEvent) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      add();
    }
  };

  const describedBy = (...ids: (string | false | undefined)[]) => ids.filter(Boolean).join(' ') || undefined;
</script>

{#if canEdit}
<form method="POST" use:enhance novalidate aria-label="Policy settings" class="flex flex-col gap-5">
  <Panel title="Policy">
            <div class="flex flex-col gap-5">
              <Field.Field>
                <Field.Label for="default-ai-use">AI use while applying</Field.Label>
                <select id="default-ai-use" name="defaultAiUse" class={selectClass} bind:value={$form.defaultAiUse} aria-describedby="default-ai-use-hint">
                  {#each aiUses as policy (policy)}<option value={policy}>{aiUseName(policy)}</option>{/each}
                </select>
                <Field.Description id="default-ai-use-hint">
                  {aiUseHint($form.defaultAiUse)} Asked on every application. A job can set its own.
                </Field.Description>
              </Field.Field>

              <Field.Field>
                <Field.Label for="device-check">Device check</Field.Label>
                <select id="device-check" name="deviceCheck" class={selectClass} bind:value={$form.deviceCheck} aria-describedby="device-check-hint">
                  {#each deviceChoices as choice (choice)}
                    <option value={choice}>{choice === 'ByRegion' ? 'By region (default)' : deviceCheckLabels[choice]}</option>
                  {/each}
                </select>
                <Field.Description id="device-check-hint">
                  Where the law needs consent, ScreenHire asks whatever you choose here.
                </Field.Description>
              </Field.Field>

              <Field.Field data-invalid={domainError || domainServerError ? true : undefined}>
                <Field.Label for="trusted-domain">Trusted email domains</Field.Label>
                <Field.Description id="trusted-domain-hint">
                  A shared email on a trusted domain, such as a staffing partner's mailbox, does not raise a duplicate.
                </Field.Description>
                {#if $form.trustedDomains.length > 0}
                  <ul class="flex flex-col gap-1" aria-label="Trusted email domains">
                    {#each $form.trustedDomains as domain (domain)}
                      <li class="flex items-center justify-between gap-2">
                        <span class="min-w-0 break-all">{domain}</span>
                        <Tooltip.Root>
                          <Tooltip.Trigger>
                            {#snippet child({ props })}
                              <Button {...props} type="button" variant="ghost" size="icon" aria-label={`Remove ${domain}`} onclick={() => ($form.trustedDomains = $form.trustedDomains.filter((entry) => entry !== domain))}>
                                <XIcon aria-hidden="true" />
                              </Button>
                            {/snippet}
                          </Tooltip.Trigger>
                          <Tooltip.Content>Remove {domain}</Tooltip.Content>
                        </Tooltip.Root>
                      </li>
                    {/each}
                  </ul>
                  <p class="text-muted-foreground tabular-nums">{$form.trustedDomains.length} of {maxTrustedEntries} domains</p>
                {/if}
                <div class="flex flex-wrap items-start gap-2">
                  <div class="min-w-48 flex-1">
                    <Input
                      id="trusted-domain"
                      autocomplete="off"
                      spellcheck="false"
                      placeholder="partner.example.com"
                      bind:value={domainInput}
                      onkeydown={onEnter(addDomain)}
                      aria-invalid={domainError || domainServerError ? 'true' : undefined}
                      aria-describedby={describedBy('trusted-domain-hint', (domainError || domainServerError) && 'trusted-domain-error')}
                    />
                  </div>
                  <Button type="button" variant="outline" size="lg" onclick={addDomain}><PlusIcon aria-hidden="true" />Add domain</Button>
                </div>
                <Field.Error id="trusted-domain-error" errors={domainError || domainServerError ? [{ message: domainError || domainServerError }] : undefined} />
              </Field.Field>

              <Field.Field data-invalid={networkError || networkServerError ? true : undefined}>
                <Field.Label for="trusted-network">Trusted networks</Field.Label>
                <Field.Description id="trusted-network-hint">
                  An office or a partner's address, such as 203.0.113.7, raises no network signal. A range works too, such as 203.0.113.0/24.
                </Field.Description>
                {#if $form.trustedNetworks.length > 0}
                  <ul class="flex flex-col gap-1" aria-label="Trusted networks">
                    {#each $form.trustedNetworks as network (network)}
                      <li class="flex items-center justify-between gap-2">
                        <span class="min-w-0 break-all tabular-nums">{network}</span>
                        <Tooltip.Root>
                          <Tooltip.Trigger>
                            {#snippet child({ props })}
                              <Button {...props} type="button" variant="ghost" size="icon" aria-label={`Remove ${network}`} onclick={() => ($form.trustedNetworks = $form.trustedNetworks.filter((entry) => entry !== network))}>
                                <XIcon aria-hidden="true" />
                              </Button>
                            {/snippet}
                          </Tooltip.Trigger>
                          <Tooltip.Content>Remove {network}</Tooltip.Content>
                        </Tooltip.Root>
                      </li>
                    {/each}
                  </ul>
                  <p class="text-muted-foreground tabular-nums">{$form.trustedNetworks.length} of {maxTrustedEntries} networks</p>
                {/if}
                <div class="flex flex-wrap items-start gap-2">
                  <div class="min-w-48 flex-1">
                    <Input
                      id="trusted-network"
                      autocomplete="off"
                      spellcheck="false"
                      class="tabular-nums"
                      placeholder="203.0.113.7"
                      bind:value={networkInput}
                      onkeydown={onEnter(addNetwork)}
                      aria-invalid={networkError || networkServerError ? 'true' : undefined}
                      aria-describedby={describedBy('trusted-network-hint', (networkError || networkServerError) && 'trusted-network-error')}
                    />
                  </div>
                  <Button type="button" variant="outline" size="lg" onclick={addNetwork}><PlusIcon aria-hidden="true" />Add network</Button>
                </div>
                <Field.Error id="trusted-network-error" errors={networkError || networkServerError ? [{ message: networkError || networkServerError }] : undefined} />
              </Field.Field>
            </div>

      {#snippet footer()}
        <div class="flex flex-wrap items-center justify-end gap-2">
          <FailureText message={$errors._errors?.[0]} class="mr-auto" />
          <p role="status" class="mr-auto text-muted-foreground empty:hidden">{$errors._errors ? '' : saved}</p>
          <Button type="submit" size="lg" disabled={$submitting}><SaveIcon aria-hidden="true" />Save</Button>
        </div>
      {/snippet}
</Panel>
</form>
{:else}
  <Panel title="Policy">
    <SummaryList>
      <SummaryItem label="AI use while applying">
        {aiUseName(detail.defaultAiUse)}. {aiUseHint(detail.defaultAiUse)} Asked on every application. A job can set its own.
      </SummaryItem>
      <SummaryItem label="Device check">{deviceCheckLabels[deviceChoiceOf()]}</SummaryItem>
      <SummaryItem label="Trusted email domains" count={detail.trustedDomains.length}>
        {#if detail.trustedDomains.length > 0}
          <ul class="flex flex-col gap-1" aria-label="Trusted email domains">
            {#each detail.trustedDomains as domain (domain)}<li class="break-all">{domain}</li>{/each}
          </ul>
        {:else}
          <span class="text-muted-foreground">None</span>
        {/if}
      </SummaryItem>
      <SummaryItem label="Trusted networks" count={detail.trustedNetworks.length}>
        {#if detail.trustedNetworks.length > 0}
          <ul class="flex flex-col gap-1" aria-label="Trusted networks">
            {#each detail.trustedNetworks as network (network)}<li class="break-all tabular-nums">{network}</li>{/each}
          </ul>
        {:else}
          <span class="text-muted-foreground">None</span>
        {/if}
      </SummaryItem>
    </SummaryList>
  </Panel>
{/if}
