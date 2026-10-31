<script lang="ts">
  import type { Question, QuestionSection } from '@screenhire/api-client';
  import {
    answeredCount,
    DEFAULT_QUESTIONS_PER_PAGE,
    formQuestions,
    PageProgress,
    QuestionField,
    questionPages,
    RESUME_QUESTION_ID,
    TextField,
    type AnswerValue,
  } from '@screenhire/ui/questions';

  // The candidate's form, rendered by the same components the careers pages render (packages/ui), in the candidate
  // theme. The apply form starts with the candidate's details and the resume; a follow-up questionnaire (`followUp`) is
  // its questions alone, as its page shows them. The candidate meets the questions a page at a time (apply-flow.md §7):
  // each named section, or pages of the organization's `questionsPerPage`. Here the pages are stacked, each opening
  // with the step line the candidate sees, so every section shows while the recruiter edits. The answers are the
  // recruiter's to try out; nothing is sent anywhere.
  let {
    questions,
    sections = null,
    questionsPerPage = DEFAULT_QUESTIONS_PER_PAGE,
    followUp = false,
  }: { questions: readonly Question[]; sections?: readonly QuestionSection[] | null; questionsPerPage?: number; followUp?: boolean } = $props();

  let answers = $state<Record<string, AnswerValue | undefined>>({});
  let firstName = $state('');
  let lastName = $state('');
  let email = $state('');
  let phone = $state('');

  const shown = $derived(followUp ? [...questions] : formQuestions(questions));
  const byId = $derived(new Map(shown.map((question) => [question.id, question])));
  const pages = $derived(questionPages(shown, sections, questionsPerPage, followUp ? [] : [RESUME_QUESTION_ID]));
  // The apply form's steps are its details, its pages and its review; a follow-up's are its pages.
  const steps = $derived(followUp ? pages.length : pages.length + 2);
  const answered = $derived(answeredCount(shown, answers));
</script>

<!-- The candidate theme: the careers site's background, ink and 15px base, as an inset in its panel. Native controls only,
     as on the real page. -->
<div class="candidate-preview flex flex-col gap-6 rounded-lg bg-candidate-background p-4 text-candidate-body text-candidate-foreground">
  {#if !followUp}
    <section aria-label="Your details" class="flex flex-col gap-4">
      <PageProgress step={1} {steps} {answered} total={shown.length} />
      <p class="text-caption text-candidate-chip-foreground">Your details</p>
      <TextField name="preview-firstName" label="First name" required autocomplete="off" bind:value={firstName} />
      <TextField name="preview-lastName" label="Last name" autocomplete="off" bind:value={lastName} />
      <TextField name="preview-email" label="Email" type="email" required autocomplete="off" bind:value={email} />
      <TextField name="preview-phone" label="Phone" type="tel" autocomplete="off" bind:value={phone} />
      {#if byId.get(RESUME_QUESTION_ID)}
        {@const resume = byId.get(RESUME_QUESTION_ID)!}
        <QuestionField question={resume} bind:answer={answers[resume.id]} />
      {/if}
    </section>
  {/if}
  {#each pages as page, i (page.questionIds.join(',') || i)}
    <section aria-label={page.name ?? `Questions, page ${i + 1}`} class="flex flex-col gap-4 border-t border-candidate-border pt-6 first:border-t-0 first:pt-0" data-preview-page>
      {#if steps > 1}<PageProgress step={followUp ? i + 1 : i + 2} {steps} {answered} total={shown.length} />{/if}
      <h3 class="font-display text-heading font-bold">{page.name ?? 'Questions'}</h3>
      {#each page.questionIds as id (id)}
        {@const question = byId.get(id)!}
        <QuestionField {question} bind:answer={answers[question.id]} />
      {/each}
    </section>
  {/each}
</div>
