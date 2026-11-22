<script lang="ts">
  import type { ActionType, Automation, ConditionOperator, DefinitionError, HiringWorkflow, TriggerType } from '@screenhire/api-client';
  import { createListEmailTemplates, createListRoles, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import PlusIcon from '~icons/lucide/plus';
  import Trash2Icon from '~icons/lucide/trash-2';
  import { tick } from 'svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import ReorderList from '#lib/components/reorder-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import {
    actionField,
    actionFieldLabels,
    actionLabels,
    actionsFor,
    builtInEmailTemplates,
    errorsAt,
    errorsUnder,
    factsFor,
    followUpsOf,
    hasTimeLimit,
    interviewFieldLabels,
    newAction,
    newRule,
    operatorLabels,
    operatorsFor,
    removeAt,
    ruleOperand,
    triggerLabels,
    triggerMayHaveQuestionnaire,
    triggerMayHaveStage,
    triggerNeedsStage,
    triggerTypes,
  } from '#lib/workflow.js';
  import { changeTrigger } from '#lib/automation-sentence.js';
  import { sendableTemplates } from '#lib/email.js';
  import { membersWith } from '#lib/team.js';
  import { getUndo } from '#lib/undo-context.js';
  import { addChipClass, checkboxClass, insetClass, labelClass, ruleSectionClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import InterviewFields from './interview-fields.svelte';

  // One automation as rules, the sheet's Rules tab under the automation's Name and On (`AutomationName`, shared with the
  // Sentence tab): When, If and Then (workflow-engine.md §2), each a well like the sentence's with its controls in rows of
  // one height, a remove button on every condition and action, and Add condition and Add action as the sentence's dashed
  // chips. Every part shows the API validator's messages for its own path, so a guardrail refusal appears on the action it
  // is about. The map's Edit on a node opens this form at that node's part: its first control carries the part's path in
  // `data-focus`. Remove automation is at the foot.
  let {
    automation = $bindable(),
    index,
    workflow,
    errors,
    onremove,
  }: { automation: Automation; index: number; workflow: HiringWorkflow; errors: readonly DefinitionError[]; onremove: () => void } = $props();

  // The emails a SendEmail action may send: the organization's list without the rejection email, or the built-in ones
  // until it loads.
  const emailTemplates = createListEmailTemplates();
  const emailChoices = $derived(emailTemplates.data ? sendableTemplates(emailTemplates.data.data.templates) : builtInEmailTemplates);

  const uid = $props.id();
  // A legend floats so it sits inside its well, like a heading, rather than on the well's edge.
  const legendClass = 'float-left w-full text-body font-semibold';
  // AssignReviewer names one of these, or nobody for the whole team. Roles without Complete tasks are left out: they could
  // not complete the review task. A reviewer who has left keeps their option until someone picks another, so the form
  // shows what is saved.
  const team = createListTeamMembers();
  const roles = createListRoles();
  const reviewers = $derived(membersWith(team.data?.data ?? [], roles.data?.data.roles ?? [], 'FillInScorecards'));
  const path = $derived(`automations[${index}]`);
  const facts = $derived(factsFor(workflow, automation.trigger.type));
  // A reviewer decision fires on the scorecards of one stage, or of any stage when it names none; a passed time limit,
  // likewise, on one stage's limit or any stage's.
  const timed = $derived(automation.trigger.type === 'SlaBreached');
  const optionalStages = $derived(timed ? workflow.stages.filter(hasTimeLimit) : workflow.stages.filter((stage) => stage.scorecard ?? stage.scorecardTemplateId));
  const factOf = (fact: string) => facts.find((option) => option.fact === fact);
  const rejects = $derived(automation.actions.some((action) => action.type === 'Reject'));
  // A completed questionnaire fires on one follow-up, or any; SendQuestionnaire names one. Without follow-ups, the action
  // is not offered (A3), unless this automation already has it.
  const followUps = $derived(followUpsOf(workflow));
  const offeredActions = (current: ActionType) =>
    actionsFor(automation.trigger.type, current).filter((type) => type !== 'SendQuestionnaire' || followUps.length > 0 || type === current);
  function setTrigger(type: TriggerType) {
    automation = changeTrigger(automation, workflow, type);
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

  // A step that asks for text (a tag, a note, a web address, a reason) and is left empty is dropped, with Undo, so a blank
  // step is never a problem to fix. A new Add a tag step starts with its tag field in focus: the tag is the first thing it
  // asks for.
  const undo = getUndo();
  const textFields = ['tag', 'note', 'url', 'reason'] as const;
  let steps = $state<HTMLElement>();

  function leaveStep(event: FocusEvent, index: number) {
    const next = event.relatedTarget;
    if (next instanceof Node && event.currentTarget instanceof HTMLElement && event.currentTarget.contains(next)) return;
    const action = automation.actions[index];
    const field = action ? actionField[action.type] : undefined;
    if (!action || !field || !textFields.includes(field as (typeof textFields)[number]) || (action[field] ?? '').trim() !== '') return;
    automation.actions = removeAt(automation.actions, index);
    undo?.offer('Removed the empty step.', async () => {
      automation.actions = [...automation.actions.slice(0, index), action, ...automation.actions.slice(index)];
      return null;
    });
  }

  async function addAction() {
    automation.actions = [...automation.actions, newAction('AddTag')];
    await tick();
    const fields = steps?.querySelectorAll<HTMLElement>('[data-step-field]');
    fields?.[fields.length - 1]?.focus();
  }
</script>

<article aria-label={automation.name || 'Untitled automation'} class="flex flex-col gap-4">
  <FieldErrors id={`${uid}-errors`} messages={[...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsAt(errors, `${path}.actions`)]} />

  <fieldset class={ruleSectionClass} aria-describedby={describedBy(errorsUnder(errors, `${path}.trigger`).length > 0 && `${uid}-trigger-errors`)}>
    <legend class={legendClass}>When</legend>
    <div class="clear-left grid items-end gap-2 @md:grid-cols-2">
      <label class={labelClass}>
        Trigger
        <select class={selectClass} value={automation.trigger.type} data-focus={`${path}.trigger`} onchange={(event) => setTrigger(event.currentTarget.value as TriggerType)}>
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
      {#if triggerMayHaveStage(automation.trigger.type)}
        <label class={labelClass}>
          Stage
          <select
            class={selectClass}
            bind:value={
              () => automation.trigger.stageId ?? '',
              (stageId) => (automation.trigger = stageId ? { type: automation.trigger.type, stageId } : { type: automation.trigger.type })
            }
          >
            <option value="">{timed ? 'Any stage with a time limit' : 'Any stage with a scorecard'}</option>
            {#each optionalStages as stage (stage.id)}<option value={stage.id}>{stage.name}</option>{/each}
            {#if automation.trigger.stageId && !optionalStages.some((stage) => stage.id === automation.trigger.stageId)}
              <option value={automation.trigger.stageId}>{automation.trigger.stageId}</option>
            {/if}
          </select>
        </label>
      {/if}
      {#if triggerMayHaveQuestionnaire(automation.trigger.type)}
        <label class={labelClass}>
          Form
          <select
            class={selectClass}
            bind:value={
              () => automation.trigger.questionnaireId ?? '',
              (questionnaireId) =>
                (automation.trigger = questionnaireId ? { type: automation.trigger.type, questionnaireId } : { type: automation.trigger.type })
            }
          >
            <option value="">Any form</option>
            {#each followUps as followUp (followUp.id)}<option value={followUp.id}>{followUp.name || followUp.id}</option>{/each}
            {#if automation.trigger.questionnaireId && !followUps.some((followUp) => followUp.id === automation.trigger.questionnaireId)}
              <option value={automation.trigger.questionnaireId}>{automation.trigger.questionnaireId}</option>
            {/if}
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

  <fieldset class={ruleSectionClass}>
    <legend class={legendClass}>If</legend>
    {#if automation.conditions}
      <label class={[labelClass, 'clear-left max-w-60']}>
        Match
        <select class={selectClass} bind:value={automation.conditions.match}>
          <option value="All">All of these hold</option>
          <option value="Any">Any of these holds</option>
        </select>
      </label>
      <ol class="flex flex-col gap-3">
        {#each automation.conditions.rules as rule, ruleIndex (ruleIndex)}
          {@const fact = factOf(rule.fact)}
          {@const rulePath = `${path}.conditions.rules[${ruleIndex}]`}
          {@const ruleErrors = errorsUnder(errors, rulePath).map((error) => error.message)}
          <li data-rule-row class="flex flex-col gap-1.5 border-t border-border-subtle pt-3 first:border-0 first:pt-0">
            <div class="grid items-end gap-2 @xl:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,1.2fr)_auto]">
              <label class={labelClass}>
                Fact <span class="sr-only">for condition {ruleIndex + 1}</span>
                <select class={selectClass} value={rule.fact} data-focus={ruleIndex === 0 ? `${path}.conditions` : undefined} onchange={(event) => setFact(ruleIndex, event.currentTarget.value)}>
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
                <!-- The hint is the grid's next cell: under Values where the row stacks, and on a line of its own under the
                     row where it does not, so the field keeps the row's one height. -->
                <label class={labelClass}>
                  Values <span class="sr-only">for condition {ruleIndex + 1}</span>
                  <Input
                    aria-describedby={`${uid}-values-${ruleIndex}-hint`}
                    value={(rule.values ?? []).join(', ')}
                    onchange={(event) =>
                      (rule.values = event.currentTarget.value
                        .split(',')
                        .map((value) => value.trim())
                        .filter(Boolean))}
                  />
                </label>
                <p id={`${uid}-values-${ruleIndex}-hint`} class="text-caption text-muted-foreground @xl:order-last @xl:col-span-full">Separate the values with commas.</p>
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
              <!-- Not and Remove share a line, also where the row stacks. -->
              <div class="flex items-center justify-between gap-2">
                <label class="flex h-9 items-center gap-1.5 text-body font-semibold pointer-coarse:h-11">
                  <input type="checkbox" class={checkboxClass} bind:checked={() => rule.negate ?? false, (on) => (rule.negate = on)} />
                  Not
                </label>
                <IconButton size="icon-lg" label={`Remove condition ${ruleIndex + 1}`} onclick={() => removeRule(ruleIndex)}><Trash2Icon /></IconButton>
              </div>
            </div>
            {#if fact?.signal}
              <p class="text-caption text-muted-foreground">
                A verification signal can route a candidate for review, never reject them.
              </p>
            {/if}
            <FieldErrors id={`${uid}-rule-${ruleIndex}-errors`} messages={ruleErrors} />
          </li>
        {/each}
      </ol>
    {:else}
      <p class="clear-left text-body text-muted-foreground">No conditions: it runs every time the trigger fires.</p>
    {/if}
    <FieldErrors
      id={`${uid}-conditions-errors`}
      messages={[...errorsAt(errors, `${path}.conditions`), ...errorsAt(errors, `${path}.conditions.match`), ...errorsAt(errors, `${path}.conditions.rules`)]}
    />
    <div><button type="button" class={addChipClass} onclick={addRule}><PlusIcon aria-hidden="true" class="size-3.5 shrink-0" />Add condition</button></div>
  </fieldset>

  <fieldset bind:this={steps} class={ruleSectionClass}>
    <legend class={legendClass}>Then</legend>
    <!-- Actions run in order and reorder with a grip (A7). They have no ids of their own, so a position names each. -->
    <ReorderList
      items={automation.actions}
      key={(action) => String(automation.actions.indexOf(action))}
      label="Actions"
      itemLabel={(action) => `action ${automation.actions.indexOf(action) + 1}`}
      onreorder={(next) => (automation.actions = next)}
      class="clear-left flex flex-col gap-3"
    >
      {#snippet item(action, grip)}
        {@const actionIndex = automation.actions.indexOf(action)}
        {@const field = actionField[action.type]}
        {@const actionPath = `${path}.actions[${actionIndex}]`}
        {@const interviewPaths = Object.keys(interviewFieldLabels).map((name) => `${actionPath}.${name}`)}
        <!-- The four interview fields show their messages under themselves (F6), so the action's own list holds the rest. -->
        {@const actionErrors = errorsUnder(errors, actionPath).filter((error) => !interviewPaths.includes(error.path)).map((error) => error.message)}
        <div data-action-row class="flex flex-col gap-2 bg-secondary" onfocusout={(event) => leaveStep(event, actionIndex)}>
          <div class="grid items-end gap-2 @xl:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_auto]">
            <label class={labelClass}>
              Action {actionIndex + 1}
              <select
                class={selectClass}
                value={action.type}
                data-focus={actionPath}
                aria-invalid={actionErrors.length > 0 || undefined}
                aria-describedby={describedBy(actionErrors.length > 0 && `${uid}-action-${actionIndex}-errors`)}
                onchange={(event) => (automation.actions[actionIndex] = newAction(event.currentTarget.value as ActionType, workflow.stages, followUps))}
              >
                {#each offeredActions(action.type) as type (type)}<option value={type}>{actionLabels[type] ?? type}</option>{/each}
              </select>
            </label>
            {#if field === 'emailTemplate'}
              <label class={labelClass}>
                {actionFieldLabels[field]}
                <select class={selectClass} bind:value={action.emailTemplate}>
                  {#each emailChoices as template (template.id)}<option value={template.id}>{template.name}</option>{/each}
                  {#if action.emailTemplate && !emailChoices.some((template) => template.id === action.emailTemplate)}
                    <!-- An archived template is out of the list, but an automation that sends it keeps it. -->
                    <option value={action.emailTemplate}
                      >{emailTemplates.data?.data.templates.find((template) => template.id === action.emailTemplate)?.name ?? 'A deleted template'}</option
                    >
                  {/if}
                </select>
              </label>
            {:else if field === 'questionnaireId'}
              <label class={labelClass}>
                {actionFieldLabels[field]}
                <select class={selectClass} bind:value={action.questionnaireId}>
                  {#each followUps as followUp (followUp.id)}<option value={followUp.id}>{followUp.name || followUp.id}</option>{/each}
                  {#if action.questionnaireId && !followUps.some((followUp) => followUp.id === action.questionnaireId)}
                    <option value={action.questionnaireId}>{action.questionnaireId}</option>
                  {/if}
                </select>
              </label>
            {:else if field === 'stageId'}
              <label class={labelClass}>
                {actionFieldLabels[field]}
                <select class={selectClass} bind:value={action.stageId}>
                  {#each workflow.stages as stage (stage.id)}<option value={stage.id}>{stage.name}</option>{/each}
                </select>
              </label>
            {:else if action.type === 'AssignReviewer'}
              <label class={labelClass}>
                Reviewer
                <select
                  class={selectClass}
                  bind:value={
                    () => action.assigneeUserId ?? '',
                    (userId) => (automation.actions[actionIndex] = userId ? { type: 'AssignReviewer', assigneeUserId: userId } : { type: 'AssignReviewer' })
                  }
                >
                  <option value="">Anyone on the team</option>
                  {#each reviewers as member (member.userId)}<option value={member.userId}>{member.displayName}</option>{/each}
                  {#if action.assigneeUserId && !reviewers.some((member) => member.userId === action.assigneeUserId)}
                    <option value={action.assigneeUserId}>{team.isSuccess ? 'No longer on the team' : 'Loading the team…'}</option>
                  {/if}
                </select>
              </label>
            {:else if field}
              <label class={labelClass}>
                {actionFieldLabels[field]}
                <Input data-step-field bind:value={() => action[field] ?? '', (value) => (action[field] = value)} maxlength={field === 'url' ? 2000 : 500} />
              </label>
            {:else}
              <!-- ScheduleInterview draws its four fields below. -->
              <span class="hidden @xl:block"></span>
            {/if}
            <div class="flex h-9 shrink-0 items-center gap-0.5 pointer-coarse:h-11">
              {#if automation.actions.length > 1}{@render grip()}{/if}
              <IconButton size="icon-lg" label={`Remove action ${actionIndex + 1}`} onclick={() => (automation.actions = removeAt(automation.actions, actionIndex))}><Trash2Icon /></IconButton>
            </div>
          </div>
          {#if action.type === 'ScheduleInterview'}
            <InterviewFields bind:action={automation.actions[actionIndex]!} {actionPath} {errors} />
          {/if}
          <!-- What no field of its own holds: the action as a whole. -->
          <FieldErrors id={`${uid}-action-${actionIndex}-errors`} messages={actionErrors} />
        </div>
      {/snippet}
    </ReorderList>
    <div><button type="button" class={addChipClass} onclick={addAction}><PlusIcon aria-hidden="true" class="size-3.5 shrink-0" />Add action</button></div>
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

  <div><Button variant="outline" size="lg" onclick={onremove}><Trash2Icon aria-hidden="true" />Remove automation</Button></div>
</article>
