<script lang="ts">
  import ListIcon from '~icons/lucide/list';
  import PlugZapIcon from '~icons/lucide/plug-zap';
  import PlusIcon from '~icons/lucide/plus';
  import SaveIcon from '~icons/lucide/save';
  import XIcon from '~icons/lucide/x';
  import type { CheckGroup, HttpValidationProblemDetails, UpdateVerificationSettingsRequest } from '@screenhire/api-client';
  import {
    ApiError,
    createGetOrganizationVerificationSettings,
    createListVerificationModels,
    createTestVerificationModel,
    createUpdateOrganizationVerificationSettings,
    getGetOrganizationVerificationSettingsHistoryQueryOptions,
    getGetOrganizationVerificationSettingsQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { checkboxClass, selectClass } from '#lib/components/workflow/controls.js';
  import { locales } from '#lib/dates.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { describeError } from '#lib/query.js';
  import {
    checkGroupHints,
    checkGroupLabels,
    deviceCheckLabels,
    domainProblem,
    maxTrustedEntries,
    modelProblemMessages,
    modelProviderLabels,
    networkProblem,
    normalizeDomain,
    type DeviceCheckChoice,
  } from '#lib/verification-settings.js';
  import { aiUseLabels } from '#lib/workflow.js';

  // The organization's verification settings (verification-engine.md section 10), in four panels: which model answers the
  // consistency check, which groups of checks run, the policies (AI use for new jobs, the device check, what is trusted)
  // and the two alerts. One form, one Save. The API key is write-only: the box is never filled from the server, and an
  // empty box keeps the saved key.
  const queryClient = useQueryClient();
  const settings = createGetOrganizationVerificationSettings();
  const detail = $derived(settings.data?.data);
  const update = createUpdateOrganizationVerificationSettings(() => ({
    mutation: { onSuccess: () => queryClient.invalidateQueries({ queryKey: getGetOrganizationVerificationSettingsQueryKey(), exact: true }) },
  }));
  const listModels = createListVerificationModels();
  const testModel = createTestVerificationModel();

  const groups = Object.keys(checkGroupLabels) as CheckGroup[];
  const checkKeys: Record<CheckGroup, 'network' | 'contact' | 'documents' | 'answers' | 'duplicates' | 'formUse'> = {
    Network: 'network',
    Contact: 'contact',
    Documents: 'documents',
    Answers: 'answers',
    Duplicates: 'duplicates',
    FormUse: 'formUse',
  };
  const aiUses = Object.keys(aiUseLabels) as (keyof typeof aiUseLabels)[];
  // The label is "Name: explanation". The name fits a phone-width select; the explanation of the chosen policy goes in the hint.
  const aiUseName = (policy: (typeof aiUses)[number]) => aiUseLabels[policy].split(': ')[0];
  const aiUseHint = (policy: (typeof aiUses)[number]) => {
    const explanation = aiUseLabels[policy].split(': ')[1] ?? '';
    return `${explanation.charAt(0).toUpperCase()}${explanation.slice(1)}.`;
  };
  const deviceChoices = Object.keys(deviceCheckLabels) as DeviceCheckChoice[];

  const trimSlashes = (url: string) => url.trim().replace(/\/+$/, '');

  const schema = z
    .object({
      llmConsistencyCheck: z.boolean(),
      monthlyTokenBudget: z
        .string()
        .trim()
        .regex(/^\d*$/, 'Enter a whole number of tokens, or leave it empty.'),
      modelSource: z.enum(['Included', 'OwnKey']),
      provider: z.enum(['Anthropic', 'OpenAiCompatible']),
      baseUrl: z.string().trim(),
      model: z.string().trim(),
      apiKey: z.string(),
      checks: z.object({ network: z.boolean(), contact: z.boolean(), documents: z.boolean(), answers: z.boolean(), duplicates: z.boolean(), formUse: z.boolean() }),
      defaultAiUse: z.enum(['Allowed', 'AllowedWithDisclosure', 'NotAllowed']),
      deviceCheck: z.enum(['ByRegion', 'Notice', 'Consent', 'Off']),
      trustedDomains: z.array(z.string()),
      trustedNetworks: z.array(z.string()),
      alerts: z.object({ suggestedEmail: z.boolean(), suggestedSlack: z.boolean(), budgetEmail: z.boolean(), budgetSlack: z.boolean() }),
    })
    .superRefine((value, context) => {
      if (value.modelSource !== 'OwnKey') return;
      if (!value.model) context.addIssue({ code: 'custom', path: ['model'], message: 'Enter the model to use.' });
      if (value.provider === 'OpenAiCompatible' && !/^https:\/\/\S+$/.test(value.baseUrl)) {
        context.addIssue({ code: 'custom', path: ['baseUrl'], message: 'Enter the full address of the endpoint, starting with https://.' });
      }
      // A saved key stays only for the same provider and address, so an old key is never sent to a new host.
      const keptKey =
        detail?.apiKeySaved === true &&
        detail.modelSource === 'OwnKey' &&
        detail.provider === value.provider &&
        trimSlashes(detail.baseUrl ?? '') === (value.provider === 'OpenAiCompatible' ? trimSlashes(value.baseUrl) : '');
      if (!value.apiKey.trim() && !keptKey) {
        context.addIssue({ code: 'custom', path: ['apiKey'], message: detail?.apiKeySaved ? 'Enter the API key for this provider.' : 'Enter the API key.' });
      }
    });

  let saved = $state('');
  let domainServerError = $state('');
  let networkServerError = $state('');
  const { form, errors, enhance, submitting, reset } = superForm(
    defaults(
      {
        llmConsistencyCheck: false,
        monthlyTokenBudget: '',
        modelSource: 'Included' as 'Included' | 'OwnKey',
        provider: 'Anthropic' as 'Anthropic' | 'OpenAiCompatible',
        baseUrl: '',
        model: '',
        apiKey: '',
        checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: true },
        defaultAiUse: 'AllowedWithDisclosure' as (typeof aiUses)[number],
        deviceCheck: 'ByRegion' as DeviceCheckChoice,
        trustedDomains: [] as string[],
        trustedNetworks: [] as string[],
        alerts: { suggestedEmail: false, suggestedSlack: false, budgetEmail: false, budgetSlack: false },
      },
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
        const own = data.modelSource === 'OwnKey';
        const budget = data.monthlyTokenBudget.trim();
        const request: UpdateVerificationSettingsRequest = {
          llmConsistencyCheck: data.llmConsistencyCheck,
          monthlyTokenBudget: budget ? Number(budget) : null,
          modelSource: data.modelSource,
          provider: own ? data.provider : null,
          baseUrl: own && data.provider === 'OpenAiCompatible' ? data.baseUrl : null,
          model: own ? data.model : null,
          apiKey: own && data.apiKey.trim() ? data.apiKey.trim() : null,
          checks: data.checks,
          defaultAiUse: data.defaultAiUse,
          deviceCheck: data.deviceCheck === 'ByRegion' ? null : data.deviceCheck,
          trustedDomains: data.trustedDomains,
          trustedNetworks: data.trustedNetworks,
          alerts: data.alerts,
        };
        try {
          await update.mutateAsync({ data: request });
          submitted.data.apiKey = ''; // the key was sent once; the box never holds a saved one
          saved = 'Saved. It applies from the next application.';
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

  // The form starts from the saved settings once they load; a refetch after that keeps what was changed.
  let started = false;
  $effect(() => {
    if (detail && !started) {
      started = true;
      reset({
        data: {
          llmConsistencyCheck: detail.llmConsistencyCheck,
          monthlyTokenBudget: detail.monthlyTokenBudget?.toString() ?? '',
          modelSource: detail.modelSource,
          provider: detail.provider ?? 'Anthropic',
          baseUrl: detail.baseUrl ?? '',
          model: detail.model ?? '',
          apiKey: '',
          checks: {
            network: detail.checks.network ?? true,
            contact: detail.checks.contact ?? true,
            documents: detail.checks.documents ?? true,
            answers: detail.checks.answers ?? true,
            duplicates: detail.checks.duplicates ?? true,
            formUse: detail.checks.formUse ?? true,
          },
          defaultAiUse: detail.defaultAiUse,
          deviceCheck: detail.deviceCheck ?? 'ByRegion',
          trustedDomains: [...detail.trustedDomains],
          trustedNetworks: [...detail.trustedNetworks],
          alerts: {
            suggestedEmail: detail.alerts.suggestedEmail ?? false,
            suggestedSlack: detail.alerts.suggestedSlack ?? false,
            budgetEmail: detail.alerts.budgetEmail ?? false,
            budgetSlack: detail.alerts.budgetSlack ?? false,
          },
        },
      });
    }
  });

  const tokens = new Intl.NumberFormat(locales);
  const own = $derived($form.modelSource === 'OwnKey');

  // Load models and Test connection use what is typed, and the saved key when the box is empty (the server decides).
  let models = $state<string[]>([]);
  let modelsNote = $state('');
  let loadingModels = $state(false);
  let testing = $state(false);
  let connected = $state(false);
  let testFailure = $state('');
  const endpoint = () => ({
    provider: $form.provider,
    baseUrl: $form.provider === 'OpenAiCompatible' ? $form.baseUrl : null,
    apiKey: $form.apiKey.trim() || null,
  });
  const endpointChanged = () => {
    connected = false;
    testFailure = '';
  };
  const firstError = (error: unknown, fallback: string) => {
    if (error instanceof ApiError && error.status === 400) {
      const first = Object.values((error.body as HttpValidationProblemDetails)?.errors ?? {})[0]?.[0];
      if (first) return first;
    }
    return describeError(error, fallback);
  };

  async function loadModels() {
    loadingModels = true;
    modelsNote = '';
    try {
      const response = await listModels.mutateAsync({ data: endpoint() });
      models = response.data.models;
      modelsNote = response.data.listed ? '' : 'This provider does not list its models. Type the model id.';
    } catch (error) {
      models = [];
      modelsNote = firstError(error, 'Could not load the models. Type the model id.');
    } finally {
      loadingModels = false;
    }
  }

  async function test() {
    testing = true;
    connected = false;
    testFailure = '';
    try {
      const response = await testModel.mutateAsync({ data: { ...endpoint(), model: $form.model } });
      if (response.data.ok) connected = true;
      else testFailure = modelProblemMessages[response.data.problem ?? 'Other'];
    } catch (error) {
      testFailure = firstError(error, 'Could not test the connection');
    } finally {
      testing = false;
    }
  }

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

  const message = (list: string[] | undefined) => list?.map((text) => ({ message: text }));
  const describedBy = (...ids: (string | false | undefined)[]) => ids.filter(Boolean).join(' ') || undefined;
</script>

<svelte:head>
  <title>Verification · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Hiring setup' }, { label: 'Verification' }]} />

<main class="flex flex-col">
  <HeaderBand title="Verification" oneRow>
    {#snippet actions()}
      {#if detail}
        <HistoryFact value={detail.lastUpdated} name="Verification settings" page={(before) => getGetOrganizationVerificationSettingsHistoryQueryOptions({ before })} />
      {/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    <div class="flex w-full max-w-form flex-col gap-5">
      {#if settings.isError && !detail}
        <Panel title="AI model">
          <QueryError message={describeError(settings.error, 'Could not load the verification settings')} onretry={() => settings.refetch()} inset />
        </Panel>
      {:else if !detail}
        <Panel title="AI model">
          <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-10 w-full" /><Skeleton class="h-14 w-full" /></div>
          <p class="sr-only" role="status">Loading the verification settings…</p>
        </Panel>
      {:else}
        <form method="POST" use:enhance novalidate aria-label="Verification settings" class="flex flex-col gap-5">
          <Panel title="AI model">
            <div class="flex flex-col gap-5">
              <div class="flex flex-col gap-2">
                <p id="model-source-label" class="text-body font-medium">Which model checks consistency</p>
                <div role="radiogroup" aria-labelledby="model-source-label" class="grid gap-2 sm:grid-cols-2">
                  <ChoiceCard name="modelSource" value="Included" bind:group={$form.modelSource} onchange={endpointChanged} title="ScreenHire's included model" description="Within a monthly token budget." />
                  <ChoiceCard name="modelSource" value="OwnKey" bind:group={$form.modelSource} onchange={endpointChanged} title="Your own key" description="Anthropic or OpenAI-compatible." />
                </div>
              </div>

              <div class="flex items-start gap-4">
                <div class="flex min-w-0 flex-1 flex-col gap-1">
                  <Field.Label for="consistency-check">Compare each application's sources</Field.Label>
                  <Field.Description id="consistency-check-hint">
                    Sends the resume, answers and any uploaded LinkedIn export to {own ? 'your AI provider' : "ScreenHire's AI provider"}.
                  </Field.Description>
                </div>
                <Switch id="consistency-check" bind:checked={$form.llmConsistencyCheck} aria-describedby="consistency-check-hint" class="mt-1" />
              </div>

              {#if own}
                <p class="text-muted-foreground">
                  Your provider handles this data under your own terms, and ScreenHire's token budget does not apply.
                </p>

                <Field.Field>
                  <Field.Label for="llm-provider">Provider</Field.Label>
                  <select id="llm-provider" name="provider" class={selectClass} bind:value={$form.provider} onchange={endpointChanged}>
                    <option value="Anthropic">{modelProviderLabels.Anthropic}</option>
                    <option value="OpenAiCompatible">{modelProviderLabels.OpenAiCompatible}</option>
                  </select>
                </Field.Field>

                {#if $form.provider === 'OpenAiCompatible'}
                  <Field.Field data-invalid={$errors.baseUrl ? true : undefined}>
                    <Field.Label for="llm-base-url">Endpoint address</Field.Label>
                    <Input
                      id="llm-base-url"
                      name="baseUrl"
                      type="url"
                      autocomplete="off"
                      placeholder="https://api.openai.com/v1/"
                      bind:value={$form.baseUrl}
                      oninput={endpointChanged}
                      aria-invalid={$errors.baseUrl ? 'true' : undefined}
                      aria-describedby={describedBy('llm-base-url-hint', $errors.baseUrl && 'llm-base-url-error')}
                    />
                    <Field.Description id="llm-base-url-hint">
                      The address of an OpenAI-compatible API, such as OpenAI or your Azure OpenAI resource's v1 address. It must be https.
                    </Field.Description>
                    <Field.Error id="llm-base-url-error" errors={message($errors.baseUrl)} />
                  </Field.Field>
                {/if}

                <Field.Field data-invalid={$errors.apiKey ? true : undefined}>
                  <Field.Label for="llm-api-key">API key</Field.Label>
                  <Input
                    id="llm-api-key"
                    name="apiKey"
                    type="password"
                    autocomplete="off"
                    spellcheck="false"
                    placeholder={detail.apiKeySaved ? 'Enter a new key to replace it' : undefined}
                    bind:value={$form.apiKey}
                    oninput={endpointChanged}
                    aria-invalid={$errors.apiKey ? 'true' : undefined}
                    aria-describedby={describedBy('llm-api-key-hint', $errors.apiKey && 'llm-api-key-error')}
                  />
                  <Field.Description id="llm-api-key-hint">
                    {#if detail.apiKeySaved && detail.modelSource === 'OwnKey'}A key is saved. Leave this empty to keep it.{:else}It is stored encrypted and never shown again.{/if}
                  </Field.Description>
                  <Field.Error id="llm-api-key-error" errors={message($errors.apiKey)} />
                </Field.Field>

                <Field.Field data-invalid={$errors.model ? true : undefined}>
                  <Field.Label for="llm-model">Model</Field.Label>
                  <div class="flex flex-wrap items-start gap-2">
                    <div class="min-w-48 flex-1">
                      <Input
                        id="llm-model"
                        name="model"
                        list="llm-model-options"
                        autocomplete="off"
                        spellcheck="false"
                        bind:value={$form.model}
                        oninput={endpointChanged}
                        aria-invalid={$errors.model ? 'true' : undefined}
                        aria-describedby={describedBy('llm-model-hint', $errors.model && 'llm-model-error')}
                      />
                      <datalist id="llm-model-options">
                        {#each models as id (id)}<option value={id}></option>{/each}
                      </datalist>
                    </div>
                    <Button type="button" variant="outline" size="lg" disabled={loadingModels} onclick={loadModels}><ListIcon aria-hidden="true" />Load models</Button>
                  </div>
                  <Field.Description id="llm-model-hint">
                    {modelsNote || (models.length > 0 ? `${models.length} models from your provider. Pick one or type an id.` : "Type the model's id, or load your provider's list.")}
                  </Field.Description>
                  <Field.Error id="llm-model-error" errors={message($errors.model)} />
                </Field.Field>

                <div class="flex flex-wrap items-center gap-3">
                  <Button type="button" variant="outline" size="lg" disabled={testing} onclick={test}><PlugZapIcon aria-hidden="true" />Test connection</Button>
                  <p role="status" class="flex items-center gap-2 text-body empty:hidden">
                    {#if testing}Testing…{:else if connected}<StatusIcon status="done" />Connected. The key and model work.{/if}
                  </p>
                  <FailureText message={testFailure} />
                </div>
                <p class="text-muted-foreground">Saving a changed key, provider, address or model tests it first.</p>
              {:else}
                <Field.Field data-invalid={$errors.monthlyTokenBudget ? true : undefined}>
                  <Field.Label for="token-budget">Monthly token budget <span class="font-normal text-muted-foreground">(optional)</span></Field.Label>
                  <Input
                    id="token-budget"
                    name="monthlyTokenBudget"
                    inputmode="numeric"
                    autocomplete="off"
                    class="tabular-nums"
                    bind:value={$form.monthlyTokenBudget}
                    aria-invalid={$errors.monthlyTokenBudget ? 'true' : undefined}
                    aria-describedby={describedBy('token-budget-hint', $errors.monthlyTokenBudget && 'token-budget-error')}
                  />
                  <Field.Description id="token-budget-hint">Empty uses ScreenHire's default of {tokens.format(detail.effectiveMonthlyTokenBudget)} tokens.</Field.Description>
                  <Field.Error id="token-budget-error" errors={message($errors.monthlyTokenBudget)} />
                </Field.Field>
              {/if}

              <SummaryList>
                <SummaryItem label="Tokens used this month"><span class="tabular-nums">{tokens.format(detail.tokensUsedThisMonth)}</span></SummaryItem>
              </SummaryList>
            </div>
          </Panel>

          <Panel title="Checks">
            <div class="flex flex-col gap-5">
              <p class="text-muted-foreground">
                A group that is off records that it did not run, so nobody reads a missing check as clean.
              </p>
              {#each groups as group (group)}
                {@const key = checkKeys[group]}
                <div class="flex items-start gap-4">
                  <div class="flex min-w-0 flex-1 flex-col gap-1">
                    <Field.Label for={`check-${key}`}>{checkGroupLabels[group]}</Field.Label>
                    <Field.Description id={`check-${key}-hint`}>{checkGroupHints[group]}</Field.Description>
                  </div>
                  <Switch id={`check-${key}`} bind:checked={$form.checks[key]} aria-describedby={`check-${key}-hint`} class="mt-1" />
                </div>
              {/each}
            </div>
          </Panel>

          <Panel title="Policy">
            <div class="flex flex-col gap-5">
              <Field.Field>
                <Field.Label for="default-ai-use">Default AI use for new jobs</Field.Label>
                <select id="default-ai-use" name="defaultAiUse" class={selectClass} bind:value={$form.defaultAiUse} aria-describedby="default-ai-use-hint">
                  {#each aiUses as policy (policy)}<option value={policy}>{aiUseName(policy)}</option>{/each}
                </select>
                <Field.Description id="default-ai-use-hint">
                  {aiUseHint($form.defaultAiUse)} A job from a template keeps the template's policy.
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
                  An office or a partner's range, such as 203.0.113.0/24 and no wider than /16, raises no network signal.
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
                      placeholder="203.0.113.0/24"
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
          </Panel>

          <Panel title="Alerts">
            <div class="flex flex-col gap-5">
              <fieldset class="flex flex-col gap-2">
                <legend class="mb-1 text-body font-medium">Verification suggested</legend>
                <p class="text-muted-foreground" id="alert-suggested-hint">
                  Tells reviewers who see the job. It names the job, never the candidate or why.
                </p>
                {#if detail.emailAvailable}
                  <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
                    <input type="checkbox" name="suggestedEmail" bind:checked={$form.alerts.suggestedEmail} aria-describedby="alert-suggested-hint" class={checkboxClass} />
                    By email
                  </label>
                {/if}
                {#if detail.slackConnected}
                  <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
                    <input type="checkbox" name="suggestedSlack" bind:checked={$form.alerts.suggestedSlack} aria-describedby="alert-suggested-hint" class={checkboxClass} />
                    On Slack, as a direct message
                  </label>
                {/if}
              </fieldset>

              <fieldset class="flex flex-col gap-2">
                <legend class="mb-1 text-body font-medium">Budget at 80%</legend>
                <p class="text-muted-foreground" id="alert-budget-hint">
                  Tells people who can change settings, once a month, at 80% of ScreenHire's token budget.
                </p>
                {#if detail.emailAvailable}
                  <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
                    <input type="checkbox" name="budgetEmail" bind:checked={$form.alerts.budgetEmail} aria-describedby="alert-budget-hint" class={checkboxClass} />
                    By email
                  </label>
                {/if}
                {#if detail.slackConnected}
                  <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
                    <input type="checkbox" name="budgetSlack" bind:checked={$form.alerts.budgetSlack} aria-describedby="alert-budget-hint" class={checkboxClass} />
                    On Slack, as a direct message
                  </label>
                {/if}
              </fieldset>

              {#if !detail.slackConnected}
                <p class="text-muted-foreground">
                  Slack is not connected. <a href="/settings/slack" class="inline-flex min-h-6 items-center underline underline-offset-2 pointer-coarse:min-h-11">Connect it in Settings</a> to send direct messages.
                </p>
              {/if}
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
      {/if}
    </div>
  </SettingsMenu>
</main>
