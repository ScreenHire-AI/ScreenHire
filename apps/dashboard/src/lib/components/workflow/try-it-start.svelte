<script lang="ts">
  import type { AiUsePolicy, HiringWorkflow } from '@screenhire/api-client';
  import { DEFAULT_QUESTIONS_PER_PAGE, type AnswerValue } from '@screenhire/ui/questions';
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import { Button } from '#lib/components/ui/button/index.js';
  import { scenariosOf, type ScenarioId } from '#lib/try-it.js';
  import OptionCard from './option-card.svelte';
  import QuestionnairePreview from './questionnaire-preview.svelte';

  // Try it's first step (design system section 11, Try it): who is applying, as big cards. A card chooses and starts at
  // once (`OptionCard`, a button). "Fill in the form yourself" opens the application form a page at a time, as the
  // candidate meets it, with Jordan Lee's details filled in; its send button hands the answers back.
  let {
    workflow,
    form = $bindable(false),
    questionsPerPage = DEFAULT_QUESTIONS_PER_PAGE,
    aiUse,
    heading = $bindable(),
    onchoose,
    onsend,
  }: {
    workflow: HiringWorkflow;
    /** The form is open in place of the cards. */
    form?: boolean;
    questionsPerPage?: number;
    aiUse?: AiUsePolicy;
    heading?: HTMLElement;
    onchoose: (scenario: Exclude<ScenarioId, 'form'>) => void;
    onsend: (answers: Record<string, AnswerValue | undefined>) => void;
  } = $props();

  const scenarios = $derived(scenariosOf(workflow));
</script>

{#if form}
  <div class="flex flex-col gap-4" data-try-it-step="form">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h3 bind:this={heading} tabindex="-1" class="text-heading focus-visible:outline-none">Fill in the form yourself</h3>
      <Button variant="outline" onclick={() => (form = false)}><ArrowLeftIcon aria-hidden="true" />Back</Button>
    </div>
    <QuestionnairePreview
      questions={workflow.questionnaire.questions}
      sections={workflow.questionnaire.sections ?? null}
      {questionsPerPage}
      {aiUse}
      details={{ firstName: 'Jordan', lastName: 'Lee', email: 'jordan@example.com' }}
      {onsend}
    />
  </div>
{:else}
  <div class="flex flex-col gap-4" data-try-it-step="start">
    <h3 bind:this={heading} tabindex="-1" class="text-heading focus-visible:outline-none">Who is applying?</h3>
    <ul class="grid gap-3 sm:grid-cols-2">
      {#each scenarios as scenario (scenario.id)}
        <li data-scenario={scenario.id}>
          <OptionCard
            title={scenario.title}
            description={scenario.description}
            onclick={() => (scenario.id === 'form' ? (form = true) : onchoose(scenario.id))}
          />
        </li>
      {/each}
    </ul>
    <p class="text-muted-foreground">Nothing real is created and no one is emailed.</p>
  </div>
{/if}
