<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import InfoButton from '#lib/components/info-button.svelte';
  import { Input } from '#lib/components/ui/input/index.js';
  import { laterStages, MAX_DAYS_LATER, sendingOf, setSendMoment, type SendMoment } from '#lib/forms-timeline.js';
  import { cn } from '#lib/utils.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';

  // When a form is sent, chosen on the form itself (workflow-engine.md section 7, Forms): when the candidate applies, when
  // they enter a stage, some days after they enter it, or only when someone sends it by hand from the candidate's page.
  // Each choice writes the automation behind it, so the Pipeline card of that stage says "Sends <form>", and the Info button
  // beside the choice says so (W14). A form an automation sends that does more than send it is not rewritten here: the line
  // says so and links to that automation.
  let {
    workflow = $bindable(),
    formId,
    automationHref,
  }: {
    workflow: HiringWorkflow;
    formId: string;
    /** Where an automation opens on the Automations tab. */
    automationHref: (automationId: string) => string;
  } = $props();

  const uid = $props.id();
  const sending = $derived(sendingOf(workflow, formId));
  const stages = $derived(laterStages(workflow));
  const kind = $derived(sending.moment.kind);
  const stageId = $derived(sending.moment.kind === 'stage' || sending.moment.kind === 'later' ? sending.moment.stageId : (stages[0]?.id ?? ''));
  const days = $derived(sending.moment.kind === 'later' ? sending.moment.days : 3);
  let typedDays = $state<number | null>(null);
  const shownDays = $derived(typedDays ?? days);
  const validDays = $derived(Number.isInteger(shownDays) && shownDays >= 1 && shownDays <= MAX_DAYS_LATER);
  const automationName = $derived(workflow.automations.find((automation) => automation.id === sending.automationId)?.name);

  function choose(moment: SendMoment) {
    typedDays = null;
    workflow = setSendMoment(workflow, formId, moment);
  }

  function chooseKind(value: string) {
    if (value === 'apply') choose({ kind: 'apply' });
    else if (value === 'hand') choose({ kind: 'hand' });
    else if (stageId) choose(value === 'later' ? { kind: 'later', stageId, days } : { kind: 'stage', stageId });
  }
</script>

{#if sending.editable}
  <div class="flex min-w-0 flex-wrap items-end gap-3">
    <div class={cn(labelClass, 'flex-[1_1_14rem]')}>
      <span class="flex items-center gap-1">
        <label for={`${uid}-when`}>When it's sent</label>
        <InfoButton
          label="About when it's sent"
          text={kind === 'hand' ? "Send it from a candidate's page, on their Application tab." : 'We add the email automation for you.'}
          class="-my-1"
        />
      </span>
      <select id={`${uid}-when`} class={selectClass} value={kind} onchange={(event) => chooseKind(event.currentTarget.value)}>
        <option value="apply">When they apply</option>
        {#if stages.length > 0}
          <option value="stage">When they enter a stage</option>
          <option value="later">Some days after they enter a stage</option>
        {/if}
        <option value="hand">Only when sent by hand</option>
      </select>
    </div>
    {#if kind === 'stage' || kind === 'later'}
      <label class={cn(labelClass, 'flex-[1_1_12rem]')}>
        Stage
        <select
          class={selectClass}
          value={stageId}
          onchange={(event) => {
            const next = event.currentTarget.value;
            choose(kind === 'later' ? { kind: 'later', stageId: next, days } : { kind: 'stage', stageId: next });
          }}
        >
          {#each stages as stage (stage.id)}<option value={stage.id}>{stage.name}</option>{/each}
        </select>
      </label>
    {/if}
    {#if kind === 'later'}
      <div class={cn(labelClass, 'flex-[0_1_8rem]')}>
        <label for={`${uid}-days`}>Days</label>
        <Input
          id={`${uid}-days`}
          type="number"
          min={1}
          max={MAX_DAYS_LATER}
          step={1}
          value={shownDays}
          oninput={(event) => {
            const next = Number(event.currentTarget.value);
            typedDays = next;
            if (Number.isInteger(next) && next >= 1 && next <= MAX_DAYS_LATER) workflow = setSendMoment(workflow, formId, { kind: 'later', stageId, days: next });
          }}
          aria-invalid={!validDays || undefined}
          aria-describedby={validDays ? undefined : `${uid}-days-error`}
        />
        <FieldErrors id={`${uid}-days-error`} messages={validDays ? [] : [`Enter a whole number from 1 to ${MAX_DAYS_LATER}.`]} />
      </div>
    {/if}
  </div>
{:else}
  <p>
    {sending.words}, by the automation
    <a class="underline underline-offset-2" href={automationHref(sending.automationId!)}>{automationName ?? 'that sends it'}</a>, which does more than send it.
    Change it there.
  </p>
{/if}
