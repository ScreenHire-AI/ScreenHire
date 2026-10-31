<script lang="ts">
  import SendIcon from '@lucide/svelte/icons/send';
  import type { ScorecardTemplateSummary, TeamMember, WorkflowStage } from '@screenhire/api-client';
  import { createRequestScorecard } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { describeError } from '#lib/query.js';
  import { scorecardWords } from '#lib/scorecard-choices.js';
  import { cn } from '#lib/utils.js';

  // Ask a teammate for a scorecard (design system section 11, Scorecard): the teammate, the scorecard and a note. It makes
  // a Scorecard task for them, done when they submit that scorecard for this candidate, never by ticking it (ADR-0017
  // decision 11). The teammates are the people who can see this job (ADR-0015) and whose role fills in scorecards; the
  // scorecard starts on the current stage's own.
  let {
    open = $bindable(false),
    applicationId,
    teammates,
    library,
    currentStage,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    /** Who can be asked: members who see the job and fill in scorecards, without the reader. Undefined while it loads. */
    teammates: readonly TeamMember[] | undefined;
    /** The scorecard library; archived ones are left out here. */
    library: readonly ScorecardTemplateSummary[];
    currentStage: WorkflowStage | undefined;
    /** After the request was made: a sentence for the page's status line. */
    ondone: (message: string) => void | Promise<void>;
  } = $props();

  const maxNoteLength = 500;
  const uid = $props.id();
  const request = createRequestScorecard();
  let picked = $state('');
  // The one teammate there is, until someone picks.
  const assigneeUserId = $derived(picked || (teammates?.length === 1 ? teammates[0].userId : ''));
  let scorecardTemplateId = $state('');
  let note = $state('');
  let tried = $state(false);
  let failure = $state('');

  // The current stage's own scorecard first, when it names one in the library, then the built-ins and the team's own.
  const stageKey = $derived(currentStage?.scorecard ? (currentStage.scorecardTemplateId ?? undefined) : undefined);
  const stageOption = $derived(
    stageKey ? { key: stageKey, label: currentStage!.scorecard!.name === currentStage!.name ? currentStage!.name : `${currentStage!.name}: ${currentStage!.scorecard!.name}` } : undefined,
  );
  const builtIns = $derived(library.filter((scorecard) => scorecard.builtIn && scorecard.id !== stageKey));
  const own = $derived(library.filter((scorecard) => !scorecard.builtIn && !scorecard.archived && scorecard.id !== stageKey));
  const scorecardName = (key: string) => (key === stageKey ? currentStage?.scorecard?.name : library.find((scorecard) => scorecard.id === key)?.name);

  // Starts fresh each time it opens, on the first teammate and the current stage's scorecard.
  $effect(() => {
    if (!open) return;
    untrack(() => {
      picked = '';
      scorecardTemplateId = stageKey ?? '';
      note = '';
      tried = false;
      failure = '';
    });
  });

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    failure = '';
    tried = true;
    if (!assigneeUserId || !scorecardTemplateId) return;
    try {
      await request.mutateAsync({ applicationId, data: { assigneeUserId, scorecardTemplateId, note: note.trim() || null } });
      const name = teammates?.find((teammate) => teammate.userId === assigneeUserId)?.displayName ?? 'your teammate';
      open = false;
      const chosen = scorecardName(scorecardTemplateId);
      await ondone(chosen ? `Asked ${name} for the ${scorecardWords(chosen)}.` : `Asked ${name} for a scorecard.`);
    } catch (error) {
      failure = describeError(error, "We couldn't ask for the scorecard just now");
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Ask for a scorecard</Sheet.Title>
      <Sheet.Description>They get a task, done when they submit the scorecard.</Sheet.Description>
    </Sheet.Header>
    <form novalidate class="flex flex-col gap-4 px-4 pb-4" {onsubmit}>
      {#if !teammates}
        <div aria-hidden="true" class="flex flex-col gap-2">
          {#each [0, 1, 2] as row (row)}<Skeleton class="h-10 w-full" />{/each}
        </div>
        <p class="sr-only" role="status">Loading your teammates…</p>
      {:else if teammates.length === 0}
        <p class="text-body">Nobody else who sees this job fills in scorecards. Add someone to the job's hiring team first.</p>
      {:else}
        <Field.Field>
          <Field.Label for={`${uid}-teammate`}>Teammate</Field.Label>
          <select
            id={`${uid}-teammate`}
            class={selectClass}
            bind:value={() => assigneeUserId, (value) => (picked = value)}
            aria-invalid={(tried && !assigneeUserId) || undefined}
            aria-describedby={tried && !assigneeUserId ? `${uid}-teammate-error` : undefined}
          >
            <option value="" disabled>Choose a teammate</option>
            {#each teammates as teammate (teammate.userId)}<option value={teammate.userId}>{teammate.displayName}</option>{/each}
          </select>
          <Field.Error id={`${uid}-teammate-error`} errors={tried && !assigneeUserId ? [{ message: 'Choose a teammate.' }] : undefined} />
        </Field.Field>
        <Field.Field>
          <Field.Label for={`${uid}-scorecard`}>Scorecard</Field.Label>
          <select
            id={`${uid}-scorecard`}
            class={selectClass}
            bind:value={scorecardTemplateId}
            aria-invalid={(tried && !scorecardTemplateId) || undefined}
            aria-describedby={tried && !scorecardTemplateId ? `${uid}-scorecard-error` : undefined}
          >
            <option value="" disabled>Choose a scorecard</option>
            {#if stageOption}
              <optgroup label="This stage">
                <option value={stageOption.key}>{stageOption.label}</option>
              </optgroup>
            {/if}
            {#if builtIns.length > 0}
              <optgroup label="Built in">
                {#each builtIns as scorecard (scorecard.id)}<option value={scorecard.id}>{scorecard.name}</option>{/each}
              </optgroup>
            {/if}
            {#if own.length > 0}
              <optgroup label="Your scorecards">
                {#each own as scorecard (scorecard.id)}<option value={scorecard.id}>{scorecard.name}</option>{/each}
              </optgroup>
            {/if}
          </select>
          <Field.Error id={`${uid}-scorecard-error`} errors={tried && !scorecardTemplateId ? [{ message: 'Choose a scorecard.' }] : undefined} />
        </Field.Field>
        <Field.Field>
          <Field.Label for={`${uid}-note`}>Note</Field.Label>
          <Textarea id={`${uid}-note`} rows={3} maxlength={maxNoteLength} bind:value={note} />
        </Field.Field>
      {/if}
      <FailureText message={failure} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        {#if teammates && teammates.length > 0}
          <Button type="submit" size="lg" disabled={request.isPending}><SendIcon aria-hidden="true" />Ask</Button>
        {/if}
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
