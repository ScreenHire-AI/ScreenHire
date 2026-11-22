<script lang="ts">
  import type { DefinitionError, HiringWorkflow, StageKind } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import Trash2Icon from '~icons/lucide/trash-2';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { stageFacts, whenOverdue } from '#lib/stage-facts.js';
  import {
    canRemindIn,
    defaultMeetingName,
    emailTemplateNames,
    errorsAt,
    errorsUnder,
    isTerminal,
    kindFromName,
    maxMeetingNameLength,
    offersMeetings,
    stageKindLabels,
    stageKinds,
    syncReminder,
    timeLimitChoices,
  } from '#lib/workflow.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import ScorecardName from './scorecard-name.svelte';
  import StageFactList from './stage-fact-list.svelte';
  import StageSchedulingFields from './stage-scheduling-fields.svelte';

  // One stage's sheet (design system section 11, Stage sheet): its name, how soon the team should respond, scorecard and
  // the automations that run there, with the stage's kind under More options. It opens in place from a stage card and
  // saves with the pipeline, so Done is the only primary (A1) and there is no subtitle. A stage's kind follows its name
  // until someone sets it by hand. Respond within is plain choices; setting one gives the stage a reminder for the team
  // (`syncReminder`), and "When overdue" under it says what runs once the time passes, or offers to add a reminder.
  let {
    workflow = $bindable(),
    stageId = $bindable(),
    errors,
    onscorecard,
    onautomations,
    onremove,
    automationsHref,
    automationHref,
    onreminder,
    onshow,
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
    /** Where one automation's line links to: it on the Automations tab. */
    automationHref?: (automationId: string) => string;
    /** Adds a reminder for when the stage's time limit passes, and opens it on the Automations tab. */
    onreminder?: (stageId: string) => void;
    /** Opens an automation on the Automations tab instead of following its link. */
    onshow?: (automationId: string) => void;
  } = $props();

  const index = $derived(workflow.stages.findIndex((stage) => stage.id === stageId));
  const stage = $derived(index >= 0 ? workflow.stages[index] : undefined);
  const first = $derived(index === 0);
  // The meeting name's refusals show under its own field; every other error about the stage shows under its name.
  const meetingNameErrors = $derived(errorsAt(errors, `stages[${index}].meetingName`));
  const stageErrors = $derived(
    errorsUnder(errors, `stages[${index}]`)
      .filter((error) => error.path !== `stages[${index}].meetingName` && !error.path.startsWith(`stages[${index}].scheduling`))
      .map((error) => error.message),
  );

  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  // What runs here, as the card says it: each email and each automation's short label. The reminder the sheet keeps for
  // the time limit is under "When overdue", so it is not listed again (W7).
  const here = $derived(stage ? stageFacts(workflow, stage, templateNames).filter((fact) => fact.kind === 'email' || fact.kind === 'automation') : []);
  const overdue = $derived(stage ? whenOverdue(workflow, stage.id, templateNames) : []);

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

  // Once a time limit is set: what runs when it passes, or, where a reminder may run, the offer to add one. A stage a check
  // moves candidates into may not hold one (the guardrail), so it says neither.
  const remindable = $derived(Boolean(stage && stage.slaDays && !isTerminal(stage.kind) && canRemindIn(workflow, stage.id)));
  // The meeting's name is optional: an empty box removes it, and the meeting is called "interview" again.
  function setMeetingName(raw: string) {
    if (!stage) return;
    stage.meetingName = raw.trim() ? raw : undefined;
  }

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
            <span id="stage-limit-label" class="text-body font-semibold text-secondary-foreground">Respond within</span>
            <ToggleGroup.Root
              type="single"
              variant="outline"
              size="lg"
              spacing={0}
              value={choice}
              onValueChange={chooseLimit}
              aria-labelledby="stage-limit-label"
              aria-describedby="stage-limit-help"
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
            <p id="stage-limit-help" class="text-muted-foreground">
              How long a candidate should wait here before your team moves them on. After that they show as Overdue. Nothing happens to the candidate.
            </p>
            {#if stage.slaDays && overdue.length > 0}
              <div data-when-overdue class="flex flex-col gap-1">
                <span class="font-semibold">When overdue</span>
                <StageFactList facts={overdue} {automationHref} {onshow} />
              </div>
            {:else if remindable}
              <p data-when-overdue class="flex flex-wrap items-center gap-x-3 gap-y-1">
                <span><span class="font-semibold">When overdue:</span> Nothing yet.</span>
                {#if onreminder}<Button variant="outline" onclick={() => stageId && onreminder(stageId)}>Add a reminder</Button>{/if}
              </p>
            {/if}
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

        {#if offersMeetings(stage)}
          <label class={labelClass}>
            What the meeting is called
            <Input
              value={stage.meetingName ?? ''}
              maxlength={maxMeetingNameLength}
              placeholder={defaultMeetingName}
              data-stage-meeting-name
              aria-invalid={meetingNameErrors.length > 0 || undefined}
              aria-describedby={meetingNameErrors.length > 0 ? 'stage-meeting-name-hint stage-meeting-name-errors' : 'stage-meeting-name-hint'}
              oninput={(event) => setMeetingName(event.currentTarget.value)}
            />
          </label>
          <p id="stage-meeting-name-hint" class="-mt-3 text-muted-foreground">Candidates see it in emails and on the page where they book, as in "Book your trial shift".</p>
          <FieldErrors id="stage-meeting-name-errors" messages={meetingNameErrors} />
          <StageSchedulingFields {stage} {index} {errors} />
        {/if}

        <div class="flex flex-col gap-2">
          <span class="text-body font-semibold text-secondary-foreground">Automations</span>
          <StageFactList facts={here} {automationHref} {onshow} />
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
