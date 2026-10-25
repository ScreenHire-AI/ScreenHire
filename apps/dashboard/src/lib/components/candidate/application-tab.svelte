<script lang="ts">
  import type { ApplicationDetail, Question } from '@screenhire/api-client';
  import AnswersPanel, { answerText } from '#lib/components/candidate/answers-panel.svelte';
  import Panel from '#lib/components/panel.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';

  // The whole application (design system section 22): the answers, each follow-up questionnaire an automation sent with
  // its answers once given, read against the questions as the candidate saw them, and the candidate's declarations.
  let { detail, questions }: { detail: ApplicationDetail; questions: ReadonlyMap<string, Question> } = $props();
</script>

<AnswersPanel {detail} {questions} />

{#each detail.followUps as followUp (followUp.id)}
  {@const asked = new Map(followUp.questions.map((question) => [question.id, question]))}
  <Panel title={`Follow-up: ${followUp.name}`}>
    {#if followUp.completedAt}
      <p class="mb-3 text-muted-foreground">Answered <RelativeTime at={followUp.completedAt} class="inline" /></p>
      <SummaryList>
        {#each followUp.answers as answer (answer.questionId)}
          {@const question = asked.get(answer.questionId)}
          <SummaryItem label={question?.label ?? answer.questionId}>
            <span class="whitespace-pre-line">{answerText(answer, question)}</span>
          </SummaryItem>
        {/each}
      </SummaryList>
      {#if followUp.answers.length === 0}<p class="text-muted-foreground">No answers to the optional questions.</p>{/if}
    {:else}
      <p class="text-muted-foreground">
        Sent <RelativeTime at={followUp.lastSentAt} class="inline" />{followUp.timesSent > 1 ? ` (${followUp.timesSent} times)` : ''}, not answered yet.
      </p>
    {/if}
  </Panel>
{/each}

<Panel title="Declarations">
  <SummaryList>
    <SummaryItem label="AI use">
      {detail.aiUseDeclaration === 'Used' ? 'Used AI' : detail.aiUseDeclaration === 'DidNotUse' ? 'Did not use AI' : 'Not asked or not answered'}{detail.aiUseDetails
        ? `: ${detail.aiUseDetails}`
        : ''}
    </SummaryItem>
    <SummaryItem label="Adjustments">{detail.accommodationRequested ? 'Asked for adjustments to the process' : 'None requested'}</SummaryItem>
  </SummaryList>
</Panel>
