<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import PlusIcon from '~icons/lucide/plus';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { MAX_DAYS_LATER, type FormLater, type TimelineForm } from '#lib/forms-timeline.js';
  import { cn } from '#lib/utils.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';

  // The forms on the candidate's timeline (design system section 11, Form timeline): one card per form, named by when it
  // reaches the candidate, in the order they meet it. A card is a choice among peers, so it is a pressed button (A6); the
  // strip scrolls sideways when forms outgrow the width (S9, F5). "Ask more later" opens a small form that asks when, then
  // adds the form and the automation that emails it, which is why it says so.
  let {
    forms,
    selected = $bindable(),
    stages,
    onadd,
  }: {
    forms: readonly TimelineForm[];
    /** `apply`, or the id of the follow-up form on show. */
    selected: string;
    /** The stages a later form can follow (not Applied, not an end stage). */
    stages: readonly WorkflowStage[];
    onadd: (later: FormLater) => void;
  } = $props();

  const uid = $props.id();
  let asking = $state(false);
  let stageId = $state('');
  let when = $state<'reach' | 'after'>('reach');
  let days = $state(3);

  const chosenStage = $derived(stages.find((stage) => stage.id === stageId) ?? stages[0]);
  const validDays = $derived(Number.isInteger(days) && days >= 1 && days <= MAX_DAYS_LATER);

  function ask() {
    stageId = stages[0]?.id ?? '';
    when = 'reach';
    days = 3;
    asking = true;
  }

  function add(event: SubmitEvent) {
    event.preventDefault();
    if (!chosenStage || (when === 'after' && !validDays)) return;
    onadd({ stageId: chosenStage.id, days: when === 'after' ? days : null });
    asking = false;
  }
</script>

<Panel title="When we ask">
  {#snippet actions()}
    {#if stages.length > 0}
      <!-- In the header, so it stays in reach when the strip scrolls sideways (S9, F5). -->
      <Button variant="outline" aria-expanded={asking} aria-controls={`${uid}-ask`} onclick={() => (asking ? (asking = false) : ask())}>
        <PlusIcon aria-hidden="true" />Ask more later
      </Button>
    {/if}
  {/snippet}
  <div class="flex min-w-0 flex-col gap-4">
    <!-- Cards keep their width and the row scrolls, so a long name never wraps a card shorter than its neighbours. -->
    <div role="group" aria-label="Forms" class="flex min-w-0 items-stretch gap-2 overflow-x-auto pb-1 [scrollbar-width:thin]">
      {#each forms as form (form.id)}
        <button
          type="button"
          data-form={form.id}
          aria-pressed={selected === form.id}
          onclick={() => (selected = form.id)}
          class={cn(
            'flex min-w-44 shrink-0 flex-col items-start gap-0.5 rounded-lg border px-3 py-2 text-left pointer-coarse:min-h-11 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
            selected === form.id ? 'border-primary bg-accent text-accent-foreground' : 'border-border bg-card hover:bg-muted',
          )}
        >
          <span class={cn('text-caption', selected === form.id ? 'text-accent-foreground' : 'text-muted-foreground')}>{form.when}</span>
          <span class="font-semibold">{form.name}</span>
        </button>
      {/each}
    </div>

    {#if asking}
      <form id={`${uid}-ask`} aria-label="Ask more later" onsubmit={add} class="flex min-w-0 flex-col gap-3 rounded-lg bg-secondary p-3">
        <div class="flex min-w-0 flex-wrap items-end gap-3">
          <label class={cn(labelClass, 'flex-[1_1_12rem]')}>
            Stage
            <select class={selectClass} bind:value={stageId}>
              {#each stages as stage (stage.id)}<option value={stage.id}>{stage.name}</option>{/each}
            </select>
          </label>
          <label class={cn(labelClass, 'flex-[1_1_14rem]')}>
            When should it go?
            <select class={selectClass} bind:value={when}>
              <option value="reach">When they reach {chosenStage?.name ?? 'it'}</option>
              <option value="after">Some days after it starts</option>
            </select>
          </label>
          {#if when === 'after'}
            <div class={cn(labelClass, 'flex-[0_1_8rem]')}>
              <label for={`${uid}-days`}>Days</label>
              <Input id={`${uid}-days`} type="number" min={1} max={MAX_DAYS_LATER} step={1} bind:value={days} aria-invalid={!validDays || undefined} aria-describedby={validDays ? undefined : `${uid}-days-error`} />
              <FieldErrors id={`${uid}-days-error`} messages={validDays ? [] : [`Enter a whole number from 1 to ${MAX_DAYS_LATER}.`]} />
            </div>
          {/if}
        </div>
        <p class="text-muted-foreground">We add the email automation for you.</p>
        <div class="flex flex-wrap gap-2">
          <Button type="submit" variant="outline" disabled={when === 'after' && !validDays}>Add form</Button>
          <Button type="button" variant="ghost" onclick={() => (asking = false)}>Cancel</Button>
        </div>
      </form>
    {/if}
  </div>
</Panel>
