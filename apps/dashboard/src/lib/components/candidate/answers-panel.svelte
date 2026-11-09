<script lang="ts" module>
  import type { ApplicationAnswer, Question } from '@screenhire/api-client';

  /** An answer in words: Yes or No, the chosen options' labels, or the text as given. */
  export function answerText(answer: ApplicationAnswer, question: Question | undefined): string {
    const values = answer.values ?? (answer.value ? [answer.value] : []);
    const options = new Map((question?.options ?? []).map((option) => [option.id, option.label]));
    if (question?.type === 'YesNo') return values.map((value) => (value === 'yes' ? 'Yes' : value === 'no' ? 'No' : value)).join(', ');
    return values.map((value) => options.get(value) ?? value).join(', ');
  }
</script>

<script lang="ts">
  import type { ApplicationDetail } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';

  // The candidate's answers to the job's questions, read against the questions as the job asks them, then their answers
  // to the questions every application asks: AI use, the assistive-technology switch and future roles. Only what they
  // declared is a row; no answer is the default and shows nothing (W6). A file answer names the file; its row, with View
  // and Download, is in the details rail (every fact has one home, W6). On Overview it links to the Application tab,
  // which holds the whole application.
  let {
    detail,
    questions,
    fullHref,
  }: {
    detail: ApplicationDetail;
    questions: ReadonlyMap<string, Question>;
    /** The Application tab, from Overview. */
    fullHref?: string;
  } = $props();

  const answers = $derived(detail.answers.filter((answer) => answer.questionId !== 'resume'));
  // Each with the question as the apply form words it. The switch is worded as the process it changes, never as what
  // the candidate uses (no inference, ADR-0004).
  const declarations = $derived(
    [
      detail.aiUseDeclaration === 'Used' || detail.aiUseDeclaration === 'DidNotUse'
        ? {
            question: 'Did you use AI tools for this application?',
            answer: detail.aiUseDeclaration === 'Used' ? `Yes${detail.aiUseDetails ? `: ${detail.aiUseDetails}` : ''}` : 'No',
          }
        : undefined,
      detail.accommodationRequested ? { question: 'Adjustments to the process', answer: 'Asked for' } : undefined,
      // Only an application whose candidate said yes can be saved. The permission has no end date, so there is none here.
      detail.futureRolesGivenAt
        ? { question: 'Keep my details for future roles', answer: detail.allowsFutureRoles ? 'Allowed' : 'Allowed, then stopped' }
        : undefined,
    ].filter((row) => row !== undefined),
  );
  const fileName = (uploadId: string | null | undefined) => detail.files.find((file) => file.uploadId === uploadId)?.fileName;
</script>

<Panel title="Answers" count={answers.length + declarations.length}>
  {#snippet actions()}
    {#if fullHref}<a href={fullHref} class="inline-flex min-h-6 items-center font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11">Full application</a>{/if}
  {/snippet}
  {#if answers.length === 0 && declarations.length === 0}
    <p class="text-muted-foreground">No answers.</p>
  {:else}
    <SummaryList>
      {#each answers as answer (answer.questionId)}
        {@const question = questions.get(answer.questionId)}
        <SummaryItem label={question?.label ?? answer.questionId} question>
          {#if question?.type === 'File'}
            {fileName(answer.value) ?? 'A file'}
          {:else}
            <span class="whitespace-pre-line">{answerText(answer, question)}</span>
          {/if}
        </SummaryItem>
      {/each}
      {#each declarations as row (row.question)}
        <SummaryItem label={row.question} question>{row.answer}</SummaryItem>
      {/each}
    </SummaryList>
  {/if}
</Panel>
