<script lang="ts">
  import type { ActionType, Automation, ConditionOperator, DefinitionError, HiringWorkflow, TriggerType } from '@screenhire/api-client';
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import IconButton from '#lib/components/icon-button.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import {
    actionField,
    actionFieldLabels,
    actionLabels,
    actionTypes,
    emailTemplates,
    errorsAt,
    errorsUnder,
    factsFor,
    moveItem,
    newAction,
    newRule,
    operatorLabels,
    operatorsFor,
    removeAt,
    ruleOperand,
    triggerLabels,
    triggerNeedsStage,
    triggerTypes,
  } from '#lib/workflow.js';
  import { checkboxClass, insetClass, kickerClass, labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';

  // One automation: trigger, conditions, actions (workflow-engine.md §2). Every part shows the API validator's messages
  // for its own path, so a guardrail refusal appears on the action it is about. Automations follow each other in the
  // sheet, divided by rules; conditions and actions are wells inside one.
  let {
    automation = $bindable(),
    index,
    workflow,
    errors,
    onremove,
  }: { automation: Automation; index: number; workflow: HiringWorkflow; errors: readonly DefinitionError[]; onremove: () => void } = $props();

  const uid = $props.id();
  const path = $derived(`automations[${index}]`);
  const facts = $derived(factsFor(workflow));
  const factOf = (fact: string) => facts.find((option) => option.fact === fact);
  const rejects = $derived(automation.actions.some((action) => action.type === 'Reject'));
  function setTrigger(type: TriggerType) {
    const stageId = triggerNeedsStage(type) ? (automation.trigger.stageId ?? workflow.stages[0]?.id ?? '') : undefined;
    automation.trigger = { type, ...(stageId !== undefined && { stageId }), ...(type === 'TimeElapsed' && { afterHours: automation.trigger.afterHours ?? 48 }) };
  }

  function addRule() {
    const rule = newRule(facts[0]!);
    automation.conditions = automation.conditions
      ? { ...automation.conditions, rules: [...automation.conditions.rules, rule] }
      : { match: 'All', rules: [rule] };
  }

  function removeRule(ruleIndex: number) {
    const rules = removeAt(automation.conditions?.rules ?? [], ruleIndex);
    automation.conditions = rules.length > 0 ? { match: automation.conditions?.match ?? 'All', rules } : null;
  }

  function setFact(ruleIndex: number, fact: string) {
    const option = factOf(fact);
    if (option) automation.conditions!.rules[ruleIndex] = newRule(option);
  }

  function setOperator(ruleIndex: number, operator: ConditionOperator) {
    const rule = automation.conditions!.rules[ruleIndex]!;
    automation.conditions!.rules[ruleIndex] = { fact: rule.fact, operator, negate: rule.negate ?? false, ...ruleOperand(operator, factOf(rule.fact)) };
  }

  const describedBy = (...ids: (string | false)[]) => ids.filter(Boolean).join(' ') || undefined;
</script>

<article aria-labelledby={`${uid}-heading`} class="flex flex-col gap-3 border-b border-border-subtle pb-6 last-of-type:border-0">
  <div class="flex items-center justify-between gap-2">
    <h3 id={`${uid}-heading`} class="min-w-0 text-heading break-words">{automation.name || 'Untitled automation'}</h3>
    <IconButton label={`Remove ${automation.name || 'this automation'}`} onclick={onremove}><Trash2Icon /></IconButton>
  </div>
  <FieldErrors id={`${uid}-errors`} messages={[...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsAt(errors, `${path}.actions`)]} />

  <div class="grid gap-2.5 @md:grid-cols-[1fr_auto]">
    <label class={labelClass}>
      Name
      <Input bind:value={automation.name} maxlength={120} aria-invalid={errorsAt(errors, `${path}.name`).length > 0 || undefined} />
    </label>
    <label class="flex items-center gap-2 pointer-coarse:min-h-11 self-end pb-1.5 text-body font-semibold">
      <input type="checkbox" class={checkboxClass} bind:checked={() => automation.enabled !== false, (on) => (automation.enabled = on)} />
      On
    </label>
  </div>
  <FieldErrors id={`${uid}-name-errors`} messages={errorsAt(errors, `${path}.name`)} />

  <fieldset class="flex flex-col gap-2" aria-describedby={describedBy(errorsUnder(errors, `${path}.trigger`).length > 0 && `${uid}-trigger-errors`)}>
    <legend class={kickerClass}>When</legend>
    <div class="grid gap-2 @xl:grid-cols-3">
      <label class={labelClass}>
        Trigger
        <select class={selectClass} value={automation.trigger.type} onchange={(event) => setTrigger(event.currentTarget.value as TriggerType)}>
          {#each triggerTypes as type (type)}<option value={type}>{triggerLabels[type]}</option>{/each}
        </select>
      </label>
      {#if triggerNeedsStage(automation.trigger.type)}
        <label class={labelClass}>
          Stage
          <select class={selectClass} bind:value={automation.trigger.stageId}>
            {#each workflow.stages as stage (stage.id)}<option value={stage.id}>{stage.name}</option>{/each}
          </select>
        </label>
      {/if}
      {#if automation.trigger.type === 'TimeElapsed'}
        <label class={labelClass}>
          After (hours)
          <Input type="number" min={1} max={8760} bind:value={automation.trigger.afterHours} />
        </label>
      {/if}
    </div>
    <FieldErrors id={`${uid}-trigger-errors`} messages={errorsUnder(errors, `${path}.trigger`).map((error) => error.message)} />
  </fieldset>

  <fieldset class="flex flex-col gap-2">
    <legend class={kickerClass}>If</legend>
    {#if automation.conditions}
      <label class={[labelClass, 'max-w-60']}>
        Match
        <select class={selectClass} bind:value={automation.conditions.match}>
          <option value="All">All of these hold</option>
          <option value="Any">Any of these holds</option>
        </select>
      </label>
      <ol class="flex flex-col gap-2">
        {#each automation.conditions.rules as rule, ruleIndex (ruleIndex)}
          {@const fact = factOf(rule.fact)}
          {@const rulePath = `${path}.conditions.rules[${ruleIndex}]`}
          {@const ruleErrors = errorsUnder(errors, rulePath).map((error) => error.message)}
          <li class={insetClass}>
            <div class="grid items-end gap-2 @2xl:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,1.2fr)_auto_auto]">
              <label class={labelClass}>
                Fact <span class="sr-only">for condition {ruleIndex + 1}</span>
                <select class={selectClass} value={rule.fact} onchange={(event) => setFact(ruleIndex, event.currentTarget.value)}>
                  {#if !fact}<option value={rule.fact}>{rule.fact}</option>{/if}
                  {#each facts as option (option.fact)}<option value={option.fact}>{option.label}</option>{/each}
                </select>
              </label>
              <label class={labelClass}>
                Test <span class="sr-only">for condition {ruleIndex + 1}</span>
                <select class={selectClass} value={rule.operator} onchange={(event) => setOperator(ruleIndex, event.currentTarget.value as ConditionOperator)}>
                  {#each fact ? operatorsFor(fact.type) : [rule.operator] as operator (operator)}<option value={operator}>{operatorLabels[operator]}</option>{/each}
                </select>
              </label>
              {#if rule.operator === 'Exists'}
                <span></span>
              {:else if rule.operator === 'In'}
                <label class={labelClass}>
                  Values, separated by commas <span class="sr-only">for condition {ruleIndex + 1}</span>
                  <Input
                    value={(rule.values ?? []).join(', ')}
                    onchange={(event) =>
                      (rule.values = event.currentTarget.value
                        .split(',')
                        .map((value) => value.trim())
                        .filter(Boolean))}
                  />
                </label>
              {:else if fact?.values}
                <label class={labelClass}>
                  Value <span class="sr-only">for condition {ruleIndex + 1}</span>
                  <select class={selectClass} bind:value={rule.value}>
                    {#each fact.values as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
                  </select>
                </label>
              {:else}
                <label class={labelClass}>
                  Value <span class="sr-only">for condition {ruleIndex + 1}</span>
                  <Input bind:value={rule.value} maxlength={200} />
                </label>
              {/if}
              <label class="flex items-center gap-1.5 pointer-coarse:min-h-11 pb-1.5 text-body font-semibold">
                <input type="checkbox" class={checkboxClass} bind:checked={() => rule.negate ?? false, (on) => (rule.negate = on)} />
                Not
              </label>
              <IconButton label={`Remove condition ${ruleIndex + 1}`} onclick={() => removeRule(ruleIndex)}><Trash2Icon /></IconButton>
            </div>
            {#if fact?.signal}
              <p class="text-caption text-muted-foreground">
                An integrity signal can route a candidate for review, never reject them.
              </p>
            {/if}
            <FieldErrors id={`${uid}-rule-${ruleIndex}-errors`} messages={ruleErrors} />
          </li>
        {/each}
      </ol>
    {:else}
      <p class="text-body text-muted-foreground">No conditions: it runs every time the trigger fires.</p>
    {/if}
    <FieldErrors
      id={`${uid}-conditions-errors`}
      messages={[...errorsAt(errors, `${path}.conditions`), ...errorsAt(errors, `${path}.conditions.match`), ...errorsAt(errors, `${path}.conditions.rules`)]}
    />
    <div><Button variant="outline" onclick={addRule}>Add condition</Button></div>
  </fieldset>

  <fieldset class="flex flex-col gap-2">
    <legend class={kickerClass}>Then</legend>
    <ol class="flex flex-col gap-2">
      {#each automation.actions as action, actionIndex (actionIndex)}
        {@const field = actionField[action.type]}
        {@const actionPath = `${path}.actions[${actionIndex}]`}
        {@const actionErrors = errorsUnder(errors, actionPath).map((error) => error.message)}
        <li class={insetClass}>
          <div class="grid items-end gap-2 @xl:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_auto]">
            <label class={labelClass}>
              Action {actionIndex + 1}
              <select
                class={selectClass}
                value={action.type}
                aria-invalid={actionErrors.length > 0 || undefined}
                aria-describedby={describedBy(actionErrors.length > 0 && `${uid}-action-${actionIndex}-errors`)}
                onchange={(event) => (automation.actions[actionIndex] = newAction(event.currentTarget.value as ActionType, workflow.stages))}
              >
                {#each actionTypes as type (type)}<option value={type}>{actionLabels[type]}</option>{/each}
              </select>
            </label>
            {#if field === 'emailTemplate'}
              <label class={labelClass}>
                {actionFieldLabels[field]}
                <select class={selectClass} bind:value={action.emailTemplate}>
                  {#each emailTemplates as template (template.id)}<option value={template.id}>{template.label}</option>{/each}
                </select>
              </label>
            {:else if field === 'stageId'}
              <label class={labelClass}>
                {actionFieldLabels[field]}
                <select class={selectClass} bind:value={action.stageId}>
                  {#each workflow.stages as stage (stage.id)}<option value={stage.id}>{stage.name}</option>{/each}
                </select>
              </label>
            {:else if field}
              <label class={labelClass}>
                {actionFieldLabels[field]}
                <Input bind:value={() => action[field] ?? '', (value) => (action[field] = value)} maxlength={field === 'url' ? 2000 : 500} />
              </label>
            {:else}
              <span></span>
            {/if}
            <div class="flex shrink-0 gap-0.5">
              {#if actionIndex > 0}
                <IconButton label={`Move action ${actionIndex + 1} up`} onclick={() => (automation.actions = moveItem(automation.actions, actionIndex, -1))}><ArrowUpIcon /></IconButton>
              {:else}<span class="size-8 pointer-coarse:size-11"></span>{/if}
              {#if actionIndex < automation.actions.length - 1}
                <IconButton label={`Move action ${actionIndex + 1} down`} onclick={() => (automation.actions = moveItem(automation.actions, actionIndex, 1))}><ArrowDownIcon /></IconButton>
              {:else}<span class="size-8 pointer-coarse:size-11"></span>{/if}
              <IconButton label={`Remove action ${actionIndex + 1}`} onclick={() => (automation.actions = removeAt(automation.actions, actionIndex))}><Trash2Icon /></IconButton>
            </div>
          </div>
          <FieldErrors id={`${uid}-action-${actionIndex}-errors`} messages={actionErrors} />
        </li>
      {/each}
    </ol>
    <div><Button variant="outline" onclick={() => (automation.actions = [...automation.actions, newAction('AddTag')])}>Add action</Button></div>
  </fieldset>

  {#if rejects}
    <div class={insetClass}>
      <label class="flex items-center gap-2 pointer-coarse:min-h-11 text-body font-semibold">
        <input type="checkbox" class={checkboxClass} bind:checked={() => automation.allowAutoReject ?? false, (on) => (automation.allowAutoReject = on)} />
        Reject without a person reviewing first
      </label>
      <p class="text-body text-muted-foreground">
        Only a knockout automation may reject: the candidate is told the reason and can ask a person to review it.
      </p>
      <FieldErrors id={`${uid}-auto-reject-errors`} messages={errorsAt(errors, `${path}.allowAutoReject`)} />
    </div>
  {/if}
</article>
