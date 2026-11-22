<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import ReplaceIcon from '~icons/lucide/replace';
  import { tick } from 'svelte';
  import type { JobDetail } from '@screenhire/api-client';
  import { ApiError, createGetJobTemplateSwitch, createListHiringTemplates, createSwitchJobTemplate } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StepMarkers, { type Step } from '#lib/components/step-markers.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, jobsPath } from '#lib/realtime.js';
  import { candidateCount, moveLines, stageMappings, suggestedChoices, type StageChoices } from '#lib/template-switch.js';
  import { templateUsagePath } from '#lib/templates.js';
  import { cn } from '#lib/utils.js';

  // Switch template (workflow-engine.md section 7, Tailoring a job): one decision at a time, with the step markers above.
  // 1. A published template. 2. Where the candidates in each stage that holds any go, preselected by the server by kind
  // and then name. 3. What changes, with the warning that the job's own pipeline edits are replaced, before Switch
  // template. The moves send no email and run no automation, which the review says once.
  let {
    open = $bindable(false),
    job,
    onswitched,
  }: {
    open?: boolean;
    job: Pick<JobDetail, 'id' | 'title' | 'template'>;
    /** The job as it is now, the template's name and how many candidates it moved. */
    onswitched?: (job: JobDetail, templateName: string, moved: number) => void;
  } = $props();

  const queryClient = useQueryClient();
  const templates = createListHiringTemplates(() => ({ query: { enabled: open } }));
  // An archived template is out of the picker, as on a new job.
  const usable = $derived((templates.data?.data ?? []).filter((template) => template.latestPublishedVersionId && !template.archived));

  let step = $state(0);
  let versionId = $state('');
  let choices = $state<StageChoices>({});
  // The template version the choices were made for.
  let choicesFor = '';
  let failure = $state('');
  let heading = $state<HTMLHeadingElement | null>(null);

  const preview = createGetJobTemplateSwitch(
    () => job.id,
    () => ({ templateVersionId: versionId }),
    () => ({ query: { enabled: open && versionId !== '' } }),
  );
  const shown = $derived(preview.data?.data);
  const switching = createSwitchJobTemplate(() => ({
    mutation: {
      onSettled: () =>
        Promise.all([invalidatePath(queryClient, jobsPath), invalidatePath(queryClient, applicationsPath), invalidatePath(queryClient, templateUsagePath)]),
    },
  }));

  // Each opening starts again from the first step.
  $effect(() => {
    if (!open) return;
    step = 0;
    failure = '';
    choices = {};
    choicesFor = '';
    versionId = '';
  });

  const steps = $derived<Step[]>(
    ['Template', 'Stages', 'Review'].map((label, index) => ({ label, state: index < step ? 'done' : index === step ? 'current' : 'upcoming' })),
  );
  const headings = ['Which template?', 'Where do the candidates go?', 'What changes'];
  const openStages = $derived((shown?.stages ?? []).filter((stage) => !['Hired', 'Rejected', 'Withdrawn'].includes(stage.kind)));

  async function go(to: number) {
    failure = '';
    // Back on the stages, the person's choices stay; another template starts again from its own suggestions.
    if (to === 1 && shown && choicesFor !== shown.templateVersionId) {
      choices = suggestedChoices(shown);
      choicesFor = shown.templateVersionId;
    }
    step = to;
    await tick();
    heading?.focus();
  }

  async function confirm() {
    if (!shown) return;
    failure = '';
    try {
      const response = await switching.mutateAsync({
        jobId: job.id,
        data: { templateVersionId: shown.templateVersionId, expectedWorkflowVersion: shown.expectedWorkflowVersion, stageMappings: stageMappings(shown, choices) },
      });
      open = false;
      onswitched?.(response.data, shown.templateName, candidateCount(shown));
    } catch (error) {
      // A 400 names what stops it (a stage left without a choice, a template that changed) in the validator's sentences.
      failure =
        error instanceof ApiError && error.status === 400
          ? Object.values((error.body as { errors?: Record<string, string[]> } | undefined)?.errors ?? {})
              .flat()
              .join(' ') || describeError(error, 'Could not switch the template')
          : describeError(error, 'Could not switch the template');
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto" data-switch-template>
    <Sheet.Header>
      <Sheet.Title>Switch template</Sheet.Title>
      <Sheet.Description>{job.title} takes another template's pipeline, and its candidates move to the stages you choose.</Sheet.Description>
    </Sheet.Header>
    <div class="flex flex-col gap-5 px-4 pb-4">
      <StepMarkers label="Steps of the switch" {steps} />
      <h3 bind:this={heading} tabindex="-1" class="text-heading focus-visible:outline-none">{headings[step]}</h3>

      {#if step === 0}
        {#if templates.isError}
          <QueryError message={describeError(templates.error, 'Could not load the templates')} onretry={() => templates.refetch()} inset />
        {:else}
          <Field.Field>
            <Field.Label for="switch-template-version">Template</Field.Label>
            <select id="switch-template-version" class={selectClass} bind:value={versionId} disabled={templates.isPending}>
              <option value="" disabled>Choose a template</option>
              {#each usable as template (template.id)}
                <option value={template.latestPublishedVersionId}>
                  {template.name} (v{template.latestPublishedVersion}){template.id === job.template?.templateId ? ", this job's template" : ''}
                </option>
              {/each}
            </select>
            <Field.Description>The job takes the template's newest published version.</Field.Description>
          </Field.Field>
        {/if}
        {#if preview.isError}
          <QueryError message={describeError(preview.error, 'Could not read the template')} onretry={() => preview.refetch()} inset />
        {/if}
      {:else if step === 1 && shown}
        {#if shown.occupied.length === 0}
          <p class="text-muted-foreground">No candidates are on this job yet, so nobody moves.</p>
        {:else}
          <div class="flex flex-col gap-4" data-stage-choices>
            {#each shown.occupied as stage (stage.stageId)}
              <Field.Field>
                <Field.Label for={`switch-stage-${stage.stageId}`}>
                  {stage.name} <span class="font-normal text-muted-foreground">({stage.candidates === 1 ? '1 candidate' : `${stage.candidates} candidates`})</span>
                </Field.Label>
                <select id={`switch-stage-${stage.stageId}`} class={selectClass} bind:value={choices[stage.stageId]}>
                  {#each shown.stages as target (target.id)}
                    <option value={target.id}>{target.name}</option>
                  {/each}
                </select>
              </Field.Field>
            {/each}
          </div>
        {/if}
      {:else if step === 2 && shown}
        <dl class="flex flex-col gap-4">
          <div class="flex flex-col gap-1">
            <dt class="font-semibold">Pipeline</dt>
            <dd>{shown.templateName} v{shown.version}: {openStages.map((stage) => stage.name).join(', ')}</dd>
          </div>
          <div class="flex flex-col gap-1">
            <dt class="font-semibold">Candidates</dt>
            <dd>
              {#if shown.occupied.length === 0}
                Nobody moves.
              {:else}
                <ul class="flex flex-col gap-1" data-move-lines>
                  {#each moveLines(shown, choices) as line (line)}<li>{line}</li>{/each}
                </ul>
              {/if}
            </dd>
          </div>
        </dl>
        <p class="text-muted-foreground">Moving them sends no emails and runs no automations. Each candidate's timeline says the pipeline changed.</p>
        <p class="font-semibold">This replaces the job's pipeline, including changes made to this job alone.</p>
        <FailureText message={failure} />
      {:else}
        <Skeleton class="h-24 w-full" />
      {/if}

      <div class="flex flex-wrap justify-end gap-2">
        {#if step === 0}
          <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
          <Button size="lg" disabled={!shown || preview.isFetching} onclick={() => go(1)}>Next<ArrowRightIcon aria-hidden="true" /></Button>
        {:else if step === 1}
          <Button variant="outline" size="lg" onclick={() => go(0)}><ArrowLeftIcon aria-hidden="true" />Back</Button>
          <Button size="lg" onclick={() => go(2)}>Next<ArrowRightIcon aria-hidden="true" /></Button>
        {:else}
          <Button variant="outline" size="lg" onclick={() => go(1)}><ArrowLeftIcon aria-hidden="true" />Back</Button>
          <Button size="lg" disabled={switching.isPending || !shown} onclick={confirm}><ReplaceIcon aria-hidden="true" />Switch template</Button>
        {/if}
      </div>
    </div>
  </Sheet.Content>
</Sheet.Root>
