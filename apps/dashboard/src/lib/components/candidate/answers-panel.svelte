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

  // The candidate's answers to the job's questions, read against the questions as the job asks them. A file answer
  // names the file; its row, with View and Download, is in the details rail (every fact has one home, W6). On Overview
  // it links to the Application tab, which holds the whole application.
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
  const fileName = (uploadId: string | null | undefined) => detail.files.find((file) => file.uploadId === uploadId)?.fileName;
</script>

<Panel title="Answers" count={answers.length}>
  {#snippet actions()}
    {#if fullHref}<a href={fullHref} class="inline-flex min-h-6 items-center font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11">Full application</a>{/if}
  {/snippet}
  {#if answers.length === 0}
    <p class="text-muted-foreground">No questionnaire answers.</p>
  {:else}
    <SummaryList>
      {#each answers as answer (answer.questionId)}
        {@const question = questions.get(answer.questionId)}
        <SummaryItem label={question?.label ?? answer.questionId}>
          {#if question?.type === 'File'}
            {fileName(answer.value) ?? 'A file'}
          {:else}
            <span class="whitespace-pre-line">{answerText(answer, question)}</span>
          {/if}
        </SummaryItem>
      {/each}
    </SummaryList>
  {/if}
</Panel>
