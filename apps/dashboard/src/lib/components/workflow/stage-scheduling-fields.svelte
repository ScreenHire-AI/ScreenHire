<script lang="ts">
  import type { DefinitionError, InterviewOption, StageScheduling, WorkflowStage } from '@screenhire/api-client';
  import { createListTeamMembers, createListTeams } from '@screenhire/api-client/svelte-query';
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import InfoButton from '#lib/components/info-button.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import WeeklyHoursEditor from '#lib/components/weekly-hours-editor.svelte';
  import { optionOrder, optionWords, weekdayHours } from '#lib/interview-options.js';
  import { durationLabel, durations, maxInterviewers, toggleInterviewer } from '#lib/schedule-interview.js';
  import { errorsUnder } from '#lib/workflow.js';
  import { checkboxClass, fieldLabelClass, labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';

  // How interviews in a stage are usually offered (design system section 11, Stage sheet; docs/architecture/workflow-engine.md
  // §2, `scheduling`): the way candidates pick a time, who interviews (a named team, which stays current, or people), the
  // length, how far ahead, and the hours. The Schedule interview sheet opens from it, so it usually needs nothing more.
  // Every part is optional, and the stage saves with the pipeline.
  let { stage, index, errors }: { stage: WorkflowStage; index: number; errors: readonly DefinitionError[] } = $props();

  const teams = createListTeams(() => ({ query: { enabled: Boolean(stage.scheduling) } }));
  const members = createListTeamMembers(() => ({ query: { enabled: Boolean(stage.scheduling) && !stage.scheduling?.interviewerTeamId } }));
  const messages = $derived(errorsUnder(errors, `stages[${index}].scheduling`).map((error) => error.message));

  // The scheduling default as it stands, changed in place on the stage the sheet edits.
  function patch(change: Partial<StageScheduling>) {
    stage.scheduling = { ...(stage.scheduling ?? {}), ...change };
  }

  const scheduling = $derived(stage.scheduling);
  const option = $derived<InterviewOption | undefined>(scheduling?.option ?? undefined);
  const who = $derived(scheduling?.interviewerTeamId ? 'team' : 'people');
</script>

<div class="flex flex-col gap-3" data-stage-scheduling>
  <span class="flex items-center gap-1">
    <span class={fieldLabelClass}>Interview scheduling</span>
    <InfoButton label="About interview scheduling" text="Set how interviews here are usually offered, so Schedule interview opens ready." />
  </span>
  {#if !scheduling}
    <Button variant="outline" class="w-fit" onclick={() => (stage.scheduling = { option: 'OurTimes', durationMinutes: 30, windowDays: 14 })}>Set a default</Button>
  {:else}
    <div class="flex flex-col gap-2">
      <span id={`stage-${index}-option-label`} class="text-body font-medium">How candidates pick a time</span>
      <div role="radiogroup" aria-labelledby={`stage-${index}-option-label`} class="flex flex-col gap-2">
        {#each optionOrder as choice (choice)}
          <ChoiceCard
            name={`stage-${index}-option`}
            value={choice}
            group={option}
            title={optionWords[choice].title}
            description={optionWords[choice].description}
            onchange={() => patch({ option: choice, useBlocks: choice === 'OurTimes' ? scheduling.useBlocks : false })}
          />
        {/each}
      </div>
    </div>

    <div class="flex flex-col gap-2">
      <span id={`stage-${index}-who-label`} class="text-body font-medium">Who interviews</span>
      <div role="radiogroup" aria-labelledby={`stage-${index}-who-label`} class="flex flex-wrap gap-x-4 gap-y-2">
        <label class="flex items-center gap-2 pointer-coarse:min-h-11">
          <input type="radio" name={`stage-${index}-who`} class="size-4 accent-primary" checked={who === 'team'}
            onchange={() => patch({ interviewerTeamId: teams.data?.data[0]?.id ?? null, interviewerUserIds: null })} />
          A team
        </label>
        <label class="flex items-center gap-2 pointer-coarse:min-h-11">
          <input type="radio" name={`stage-${index}-who`} class="size-4 accent-primary" checked={who === 'people'}
            onchange={() => patch({ interviewerTeamId: null })} />
          People
        </label>
      </div>
      {#if who === 'team'}
        <label class={labelClass}>
          Team
          <select class={selectClass} value={scheduling.interviewerTeamId ?? ''} onchange={(event) => patch({ interviewerTeamId: event.currentTarget.value || null })}>
            {#each teams.data?.data ?? [] as team (team.id)}<option value={team.id}>{team.name}</option>{/each}
          </select>
        </label>
        <p class="text-muted-foreground">The team's people who are on a job's hiring team start ticked when you schedule.</p>
      {:else}
        <fieldset class="flex min-w-0 flex-col gap-1">
          <legend class="sr-only">Interviewers</legend>
          {#each members.data?.data ?? [] as member (member.id)}
            <label class="flex min-w-0 items-center gap-2 pointer-coarse:min-h-11">
              <input
                type="checkbox"
                class={checkboxClass}
                checked={(scheduling.interviewerUserIds ?? []).includes(member.userId)}
                disabled={!(scheduling.interviewerUserIds ?? []).includes(member.userId) && (scheduling.interviewerUserIds ?? []).length >= maxInterviewers}
                onchange={(event) => {
                  const next = toggleInterviewer(scheduling.interviewerUserIds ?? [], member.userId, event.currentTarget.checked);
                  patch({ interviewerUserIds: next.length > 0 ? next : null });
                }}
              />
              <span class="min-w-0 break-words">{member.displayName}</span>
            </label>
          {/each}
        </fieldset>
      {/if}
    </div>

    <div class="grid gap-3 sm:grid-cols-2">
      <label class={labelClass}>
        Length
        <select class={selectClass} value={String(scheduling.durationMinutes ?? 30)} onchange={(event) => patch({ durationMinutes: Number(event.currentTarget.value) })}>
          {#each durations as minutes (minutes)}<option value={String(minutes)}>{durationLabel(minutes)}</option>{/each}
        </select>
      </label>
      <label class={labelClass}>
        Days ahead
        <Input type="number" min={1} max={60} value={scheduling.windowDays ?? 14} oninput={(event) => patch({ windowDays: Number(event.currentTarget.value) || null })} />
      </label>
    </div>

    {#if option !== 'OurSuggestion'}
      {#if scheduling.hours}
        <WeeklyHoursEditor
          value={scheduling.hours.days}
          id={`stage-${index}-hours`}
          legend={scheduling.useBlocks ? 'Interview blocks' : 'Hours'}
          onchange={(days) => patch({ hours: { days } })}
        />
        {#if option === 'OurTimes' || option === undefined}
          <label class="flex items-center gap-2 pointer-coarse:min-h-11">
            <input type="checkbox" class={checkboxClass} checked={scheduling.useBlocks ?? false} onchange={(event) => patch({ useBlocks: event.currentTarget.checked })} />
            These are interview blocks: offer them whatever the calendars say
          </label>
        {/if}
        <Button variant="outline" class="w-fit" onclick={() => patch({ hours: null, useBlocks: false })}>Use weekdays, 9 to 5</Button>
      {:else}
        <p class="text-muted-foreground">Hours: weekdays, 9 to 5.</p>
        <Button variant="outline" class="w-fit" onclick={() => patch({ hours: { days: weekdayHours() } })}>Set the hours</Button>
      {/if}
    {/if}

    <FieldErrors id={`stage-${index}-scheduling-errors`} {messages} />
    <Button variant="outline" class="w-fit" onclick={() => (stage.scheduling = null)}>Remove the default</Button>
  {/if}
</div>
