<script lang="ts">
  import type { ApplicationDetail, Question } from '@screenhire/api-client';
  import AnswersPanel, { answerText } from '#lib/components/candidate/answers-panel.svelte';
  import Panel from '#lib/components/panel.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';

  // The whole application (design system section 21): the answers, each follow-up questionnaire an automation sent with
  // its answers once given, read against the questions as the candidate saw them, and the candidate's declarations.
  let { detail, questions }: { detail: ApplicationDetail; questions: ReadonlyMap<string, Question> } = $props();

  const aiUse = $derived(detail.aiUseDeclaration === 'Used' ? 'Used AI' : detail.aiUseDeclaration === 'DidNotUse' ? 'Did not use AI' : undefined);
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

<!-- Only what the candidate declared is a row: no AI answer and no adjustments are the default, so they show nothing (W6). -->
<Panel title="Declarations">
  <SummaryList>
    {#if aiUse}
      <SummaryItem label="AI use">{aiUse}{detail.aiUseDetails ? `: ${detail.aiUseDetails}` : ''}</SummaryItem>
    {/if}
    {#if detail.accommodationRequested}
      <SummaryItem label="Adjustments">Asked for adjustments to the process</SummaryItem>
    {/if}
    <!-- "Keep my details for future roles": only an application whose candidate said yes can be saved. The
         default, no, shows nothing (W6). The permission has no end date, so there are no dates here. -->
    {#if detail.futureRolesGivenAt}
      <SummaryItem label="Future roles">
        {#if detail.allowsFutureRoles}Allowed{:else}Allowed, then stopped{/if}
      </SummaryItem>
    {/if}
  </SummaryList>
  {#if !aiUse && !detail.accommodationRequested && !detail.futureRolesGivenAt}
    <p class="text-muted-foreground">Nothing declared.</p>
  {/if}
</Panel>
