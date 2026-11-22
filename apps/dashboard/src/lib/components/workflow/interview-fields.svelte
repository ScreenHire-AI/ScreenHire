<script lang="ts">
  import type { AutomationAction, DefinitionError } from '@screenhire/api-client';
  import { createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import { Input } from '#lib/components/ui/input/index.js';
  import { errorsAt, interviewFieldLabels, maxInterviewWindowDays } from '#lib/workflow.js';
  import { durationLabel, durations, maxInterviewers, timeZoneOptions, toggleInterviewer } from '#lib/schedule-interview.js';
  import { optionWords } from '#lib/interview-options.js';
  import { zoneName } from '#lib/report-range.js';
  import { checkboxClass, labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';

  // A ScheduleInterview action's fields, for the automation's sentence and for its full form alike. First what it does when it
  // runs: send the invitation, or give the team a task to choose how the candidate schedules (which needs nothing else).
  // Sending takes who, how long, how many days to choose and which time zone, and how the candidate picks: the stage's
  // default unless the author chooses. We'll suggest a time needs a person to pick the times, so it is not offered here.
  // Each field shows the API validator's messages for its own path (F6).
  let {
    action = $bindable(),
    actionPath,
    errors,
  }: { action: AutomationAction; actionPath: string; errors: readonly DefinitionError[] } = $props();

  const uid = $props.id();
  const team = createListTeamMembers();
  const at = (name: keyof typeof interviewFieldLabels) => errorsAt(errors, `${actionPath}.${name}`);
  const describedBy = (...ids: (string | false)[]) => ids.filter(Boolean).join(' ') || undefined;

  const chosen = $derived(action.interviewerUserIds ?? []);
  const people = $derived(team.data?.data ?? []);
  const gone = $derived(chosen.filter((id) => !people.some((member) => member.userId === id)));
  const zoneId = $derived(action.interviewTimeZone ?? '');
  const zones = $derived(timeZoneOptions(zoneId));
  const choosing = $derived(action.interviewMode === 'Choose');

  // Choosing needs no other field, and the validator refuses one it would ignore, so the switch clears them, and back.
  function setMode(mode: 'Send' | 'Choose') {
    action =
      mode === 'Choose'
        ? { type: 'ScheduleInterview', interviewMode: 'Choose' }
        : { type: 'ScheduleInterview', interviewerUserIds: [], interviewMinutes: 30, interviewWindowDays: 14, interviewTimeZone: zoneName() };
  }
</script>

<div class="mt-3 flex flex-col gap-3">
  <div class="flex flex-col gap-1">
    <span id={`${uid}-mode-label`} class={labelClass}>When this runs</span>
    <div role="radiogroup" aria-labelledby={`${uid}-mode-label`} class="flex flex-col gap-1">
      <label class="flex min-w-0 items-center gap-2 py-0.5 pointer-coarse:min-h-11">
        <input type="radio" name={`${uid}-mode`} class="size-4 accent-primary" checked={!choosing} onchange={() => setMode('Send')} />
        Send the invitation
      </label>
      <label class="flex min-w-0 items-center gap-2 py-0.5 pointer-coarse:min-h-11">
        <input type="radio" name={`${uid}-mode`} class="size-4 accent-primary" checked={choosing} onchange={() => setMode('Choose')} />
        Create a task to choose how they schedule
      </label>
    </div>
  </div>
  {#if choosing}
    <p class="text-muted-foreground">The hiring team gets a task, and it is done once someone offers the interview.</p>
  {:else}
  <div class="flex min-w-0 flex-col gap-1">
    <label class={labelClass}>
      How they pick a time
      <select
        class={selectClass}
        value={action.interviewOption ?? ''}
        aria-invalid={errorsAt(errors, `${actionPath}.interviewOption`).length > 0 || undefined}
        onchange={(event) => (action.interviewOption = (event.currentTarget.value || undefined) as typeof action.interviewOption)}
      >
        <option value="">The stage's default</option>
        <option value="OurTimes">{optionWords.OurTimes.title}</option>
        <option value="TheirTimes">{optionWords.TheirTimes.title}</option>
      </select>
    </label>
    <FieldErrors id={`${uid}-option-errors`} messages={errorsAt(errors, `${actionPath}.interviewOption`)} />
  </div>
  <fieldset
    class="flex min-w-0 flex-col gap-1"
    aria-describedby={describedBy(`${uid}-interviewers-hint`, at('interviewerUserIds').length > 0 && `${uid}-interviewers-errors`)}
  >
    <legend class={labelClass}>{interviewFieldLabels.interviewerUserIds}</legend>
    {#each people as member (member.userId)}
      <label class="flex min-w-0 items-center gap-2 py-0.5 pointer-coarse:min-h-11">
        <input
          type="checkbox"
          class={checkboxClass}
          checked={chosen.includes(member.userId)}
          disabled={!chosen.includes(member.userId) && chosen.length >= maxInterviewers}
          onchange={(event) => (action.interviewerUserIds = toggleInterviewer(chosen, member.userId, event.currentTarget.checked))}
        />
        <span class="min-w-0 break-words">{member.displayName}</span>
      </label>
    {/each}
    <!-- Someone who left the team stays ticked until the person unticks them, so the form shows what is saved. -->
    {#each gone as userId (userId)}
      <label class="flex min-w-0 items-center gap-2 py-0.5 pointer-coarse:min-h-11">
        <input type="checkbox" class={checkboxClass} checked onchange={(event) => (action.interviewerUserIds = toggleInterviewer(chosen, userId, event.currentTarget.checked))} />
        <span class="min-w-0 break-words text-muted-foreground">{team.isSuccess ? 'No longer on the team' : 'Loading the team…'}</span>
      </label>
    {/each}
    {#if people.length === 0 && gone.length === 0}
      <p class="text-muted-foreground">{team.isPending ? 'Loading the team…' : 'No teammates to choose from.'}</p>
    {/if}
    <p id={`${uid}-interviewers-hint`} class="text-muted-foreground">
      The first person you choose sends the invitation. For times from calendars, everyone needs a calendar connected or their interview hours set when it runs.
    </p>
    <FieldErrors id={`${uid}-interviewers-errors`} messages={at('interviewerUserIds')} />
  </fieldset>
  <div class="grid items-start gap-2 @xl:grid-cols-3">
    <div class="flex min-w-0 flex-col gap-1">
      <label class={labelClass}>
        {interviewFieldLabels.interviewMinutes}
        <select
          class={selectClass}
          aria-invalid={at('interviewMinutes').length > 0 || undefined}
          aria-describedby={describedBy(at('interviewMinutes').length > 0 && `${uid}-minutes-errors`)}
          bind:value={action.interviewMinutes}
        >
          {#each durations as minutes (minutes)}<option value={minutes}>{durationLabel(minutes)}</option>{/each}
          {#if action.interviewMinutes && !(durations as readonly number[]).includes(action.interviewMinutes)}
            <option value={action.interviewMinutes}>{action.interviewMinutes} minutes</option>
          {/if}
        </select>
      </label>
      <FieldErrors id={`${uid}-minutes-errors`} messages={at('interviewMinutes')} />
    </div>
    <div class="flex min-w-0 flex-col gap-1">
      <label class={labelClass}>
        {interviewFieldLabels.interviewWindowDays}
        <Input
          type="number"
          min={1}
          max={maxInterviewWindowDays}
          aria-invalid={at('interviewWindowDays').length > 0 || undefined}
          aria-describedby={describedBy(at('interviewWindowDays').length > 0 && `${uid}-days-errors`)}
          bind:value={action.interviewWindowDays}
        />
      </label>
      <FieldErrors id={`${uid}-days-errors`} messages={at('interviewWindowDays')} />
    </div>
    <div class="flex min-w-0 flex-col gap-1">
      <label class={labelClass}>
        {interviewFieldLabels.interviewTimeZone}
        <select
          class={selectClass}
          aria-invalid={at('interviewTimeZone').length > 0 || undefined}
          aria-describedby={describedBy(at('interviewTimeZone').length > 0 && `${uid}-zone-errors`)}
          bind:value={action.interviewTimeZone}
        >
          {#each zones as zone (zone.value)}<option value={zone.value}>{zone.label}</option>{/each}
          {#if zoneId && !zones.some((zone) => zone.value === zoneId)}<option value={zoneId}>{zoneId}</option>{/if}
        </select>
      </label>
      <FieldErrors id={`${uid}-zone-errors`} messages={at('interviewTimeZone')} />
    </div>
  </div>
  <p class="text-muted-foreground">The window opens when the automation runs.</p>
  {/if}
</div>
