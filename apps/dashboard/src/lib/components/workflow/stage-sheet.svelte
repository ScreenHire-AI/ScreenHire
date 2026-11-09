<script lang="ts">
  import type { DefinitionError, HiringWorkflow, StageKind } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import Trash2Icon from '~icons/lucide/trash-2';
  import ZapIcon from '~icons/lucide/zap';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { automationsAt } from '#lib/journey-text.js';
  import {
    canRemindIn,
    emailTemplateNames,
    errorsUnder,
    isTerminal,
    kindFromName,
    stageKindLabels,
    stageKinds,
    syncReminder,
    timeLimitChoices,
    timeLimitLabel,
  } from '#lib/workflow.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import ScorecardName from './scorecard-name.svelte';

  // One stage's sheet (design system section 11, Stage sheet): its name, time limit, scorecard and the automations that
  // run there, with the stage's kind under More options. It opens in place from a stage card or an Overview column and
  // saves with the workflow, so Done is the only primary (A1) and there is no subtitle. A stage's kind follows its name
  // until someone sets it by hand. The time limit is plain choices; setting one gives the stage a reminder for the team
  // (`syncReminder`), which is what the sentence under the choices says.
  let {
    workflow = $bindable(),
    stageId = $bindable(),
    errors,
    onscorecard,
    onautomations,
    onremove,
    automationsHref,
  }: {
    workflow: HiringWorkflow;
    /** The stage the sheet shows, or null while it is closed. */
    stageId: string | null;
    errors: readonly DefinitionError[];
    /** Opens the scorecard sheet for the stage at this index. */
    onscorecard: (index: number) => void;
    /** Opens the automations drawer for the stage, to add one. */
    onautomations: (stageId: string) => void;
    /** Removes the stage at this index. */
    onremove: (index: number) => void;
    /** The Automations tab, where every automation of the workflow is listed. */
    automationsHref: string;
  } = $props();

  const index = $derived(workflow.stages.findIndex((stage) => stage.id === stageId));
  const stage = $derived(index >= 0 ? workflow.stages[index] : undefined);
  const first = $derived(index === 0);
  const stageErrors = $derived(errorsUnder(errors, `stages[${index}]`).map((error) => error.message));

  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  // The reminder the sheet keeps for the time limit is the sentence under the choices, so it is not listed again (W7).
  const here = $derived(stageId ? automationsAt(workflow, stageId, templateNames, false) : []);

  let otherChosen = $state(false);
  let more = $state(false);
  // Which stage the local choices belong to, so they reset when the sheet opens another.
  let shownFor: string | null = null;
  $effect(() => {
    if (stageId === shownFor) return;
    shownFor = stageId;
    otherChosen = false;
    more = false;
  });

  const standard = (days: number | null | undefined) => timeLimitChoices.some((choice) => choice.days === (days ?? null));
  const choice = $derived(otherChosen || (stage?.slaDays != null && !standard(stage.slaDays)) ? 'other' : String(stage?.slaDays ?? 'none'));

  // The workflow with the reminder in step: set the automations, which the editor's bound workflow shares.
  function syncReminders() {
    if (!stage) return;
    const next = syncReminder(workflow, stage.id);
    if (next !== workflow) workflow.automations = next.automations;
  }

  function chooseLimit(value: string | undefined) {
    if (!stage || value === undefined || value === '') return;
    if (value === 'other') {
      otherChosen = true;
      return;
    }
    otherChosen = false;
    stage.slaDays = value === 'none' ? null : Number(value);
    syncReminders();
  }

  function setOther(raw: string) {
    if (!stage) return;
    stage.slaDays = raw ? Math.max(1, Math.min(365, Math.round(Number(raw)))) : null;
    syncReminders();
  }

  // The kind follows the name until it is set by hand: only a kind that is what the old name gave moves with the new one.
  function rename(name: string) {
    if (!stage) return;
    const followed = !first && stage.kind === kindFromName(stage.name);
    stage.name = name;
    if (followed) stage.kind = kindFromName(name);
    syncReminders();
  }

  // A stage set by hand to one that ends the application loses its reminder, as a rename to one does.
  function setKind(kind: StageKind) {
    if (!stage) return;
    stage.kind = kind;
    syncReminders();
  }

  const reminderSaid = $derived(
    stage && stage.slaDays && !isTerminal(stage.kind) && canRemindIn(workflow, stage.id) ? `The team gets a reminder after ${timeLimitLabel(stage.slaDays)}.` : null,
  );
  const hasScorecard = $derived(Boolean(stage?.scorecard || stage?.scorecardTemplateId));
</script>

<Sheet.Root open={stageId !== null} onOpenChange={(open) => !open && (stageId = null)}>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-md">
    <Sheet.Header>
      <Sheet.Title class="break-words">{stage?.name || 'Stage'}</Sheet.Title>
    </Sheet.Header>
    {#if stage}
      <div class="flex flex-col gap-5 px-4">
        <label class={labelClass}>
          Name
          <Input
            value={stage.name}
            maxlength={80}
            data-stage-name
            aria-invalid={stageErrors.length > 0 || undefined}
            oninput={(event) => rename(event.currentTarget.value)}
          />
        </label>
        <FieldErrors id="stage-sheet-errors" messages={stageErrors} />

        {#if !isTerminal(stage.kind)}
          <div class="flex flex-col gap-2">
            <span id="stage-limit-label" class="text-body font-semibold text-secondary-foreground">Time limit</span>
            <ToggleGroup.Root
              type="single"
              variant="outline"
              size="lg"
              spacing={0}
              value={choice}
              onValueChange={chooseLimit}
              aria-labelledby="stage-limit-label"
              class="flex max-w-full flex-wrap"
            >
              {#each timeLimitChoices as option (option.label)}
                <ToggleGroup.Item value={String(option.days ?? 'none')} class="text-foreground data-[state=on]:inset-ring-2 data-[state=on]:inset-ring-accent-foreground">{option.label}</ToggleGroup.Item>
              {/each}
              <ToggleGroup.Item value="other" class="text-foreground data-[state=on]:inset-ring-2 data-[state=on]:inset-ring-accent-foreground">Other</ToggleGroup.Item>
            </ToggleGroup.Root>
            {#if choice === 'other'}
              <label class={[labelClass, 'w-full max-w-40']}>
                Days
                <Input type="number" min={1} max={365} value={stage.slaDays ?? ''} onchange={(event) => setOther(event.currentTarget.value)} />
              </label>
            {/if}
            {#if reminderSaid}<p class="text-muted-foreground">{reminderSaid}</p>{/if}
          </div>

          <div class="flex flex-col gap-2">
            <span class="text-body font-semibold text-secondary-foreground">Scorecard</span>
            <div class="flex items-center justify-between gap-3 rounded-lg bg-secondary p-3">
              <span class={['min-w-0 break-words', !hasScorecard && 'text-muted-foreground']}>
                {#if hasScorecard}<ScorecardName {stage} />{:else}None yet{/if}
              </span>
              <Button variant="outline" onclick={() => onscorecard(index)}>{hasScorecard ? 'Change' : 'Choose'}{' '}<span class="sr-only">scorecard for {stage.name}</span></Button>
            </div>
          </div>
        {/if}

        <div class="flex flex-col gap-2">
          <span class="text-body font-semibold text-secondary-foreground">Automations</span>
          {#if here.length > 0}
            <ul class="flex flex-col gap-1.5">
              {#each here as line (line.index)}
                <li class="flex items-start gap-1.5 break-words"><ZapIcon aria-hidden="true" class="mt-0.5 shrink-0 text-primary" /><span class="min-w-0">{line.text}</span></li>
              {/each}
            </ul>
          {/if}
          <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
            <Button variant="outline" onclick={() => stageId && onautomations(stageId)}>Add an automation</Button>
            <a href={automationsHref} class="underline underline-offset-2 pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center" onclick={() => (stageId = null)}>Open in Automations</a>
          </div>
        </div>

        <div class="flex flex-col gap-3">
          <button
            type="button"
            class="flex w-full items-center justify-between gap-2 rounded-lg py-1 text-left font-semibold text-secondary-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            aria-expanded={more}
            aria-controls="stage-more-options"
            onclick={() => (more = !more)}
          >
            More options
            <ChevronDownIcon aria-hidden="true" class={['transition-transform', more && 'rotate-180']} />
          </button>
          {#if more}
            <div id="stage-more-options" class="flex flex-col gap-2">
              <label class={labelClass}>
                Kind
                <select class={selectClass} value={stage.kind} onchange={(event) => setKind(event.currentTarget.value as StageKind)}>
                  {#each stageKinds as kind (kind)}<option value={kind}>{stageKindLabels[kind]}</option>{/each}
                </select>
              </label>
            </div>
          {/if}
        </div>
      </div>
    {/if}
    <Sheet.Footer class="flex-row items-center justify-end">
      {#if stage && !first}
        <MoreMenu label={`More for ${stage.name}`} size="icon-lg">
          <DropdownMenu.Item
            onSelect={() => {
              const at = index;
              stageId = null;
              onremove(at);
            }}
          >
            <Trash2Icon aria-hidden="true" />Remove stage
          </DropdownMenu.Item>
        </MoreMenu>
      {/if}
      <Button size="lg" onclick={() => (stageId = null)}>Done</Button>
    </Sheet.Footer>
  </Sheet.Content>
</Sheet.Root>
