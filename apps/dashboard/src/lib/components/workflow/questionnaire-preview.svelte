<script lang="ts">
  import type { AiUsePolicy, Question, QuestionSection } from '@screenhire/api-client';
  import {
    DEFAULT_QUESTIONS_PER_PAGE,
    formQuestions,
    PageProgress,
    QuestionField,
    questionPages,
    RESUME_QUESTION_ID,
    TextField,
    type AnswerValue,
    type QuestionPage,
  } from '@screenhire/ui/questions';
  import { tick, untrack } from 'svelte';

  // The candidate's form, rendered by the same components the careers pages render (packages/ui), in the candidate
  // theme. The apply form starts with the candidate's details and the resume and ends with the review step, which asks
  // about AI use; a follow-up questionnaire (`followUp`) is its questions alone, as its page shows them. The candidate
  // meets the questions a page at a time (apply-flow.md §7): each named section, or pages of the organization's
  // `questionsPerPage`. The preview pages the same way, one step at a time with Back and Next, each step opening with the
  // progress bar the candidate sees. The answers are the recruiter's to try out; nothing is sent anywhere, so the last
  // step's send button is off and says so. Try it (`onsend`) turns it on: pressing it hands the answers back, still sending
  // nothing, and `details` fills the candidate's details with its sample candidate.
  let {
    questions,
    sections = null,
    questionsPerPage = DEFAULT_QUESTIONS_PER_PAGE,
    followUp = false,
    aiUse,
    details,
    onsend,
  }: {
    questions: readonly Question[];
    sections?: readonly QuestionSection[] | null;
    questionsPerPage?: number;
    followUp?: boolean;
    /** The apply form's last step asks whether AI tools were used, under this policy; undefined leaves it out. */
    aiUse?: AiUsePolicy;
    /** The candidate's details to start from, so a try-out's name matches the rest of it. */
    details?: { firstName: string; lastName: string; email: string };
    /** Turns the last step's send button on: it hands the answers back. Nothing is sent anywhere. */
    onsend?: (answers: Record<string, AnswerValue | undefined>) => void;
  } = $props();

  type Step = { kind: 'details' } | { kind: 'page'; page: QuestionPage; number: number } | { kind: 'review' };

  let answers = $state<Record<string, AnswerValue | undefined>>({});
  let firstName = $state(untrack(() => details?.firstName ?? ''));
  let lastName = $state(untrack(() => details?.lastName ?? ''));
  let email = $state(untrack(() => details?.email ?? ''));
  let phone = $state('');
  let chosen = $state(0);
  let heading = $state<HTMLElement>();

  const shown = $derived(followUp ? [...questions] : formQuestions(questions));
  const byId = $derived(new Map(shown.map((question) => [question.id, question])));
  const pages = $derived(questionPages(shown, sections, questionsPerPage, followUp ? [] : [RESUME_QUESTION_ID]));
  // The apply form's steps are its details, its pages and its review; a follow-up's are its pages.
  const steps = $derived<Step[]>([
    ...(followUp ? [] : [{ kind: 'details' } as const]),
    ...pages.map((page, index) => ({ kind: 'page' as const, page, number: index + 1 })),
    ...(followUp ? [] : [{ kind: 'review' } as const]),
  ]);
  // An edit that removes pages keeps the preview on a step that still exists.
  const at = $derived(Math.max(0, Math.min(chosen, steps.length - 1)));
  const step = $derived(steps[at]);
  const last = $derived(at === steps.length - 1);

  // Moving between steps puts the focus on the new step's heading, as a new page would.
  async function go(to: number) {
    chosen = Math.max(0, Math.min(to, steps.length - 1));
    await tick();
    heading?.focus();
  }

  // The careers page's .button-primary and .button-secondary, in the same candidate tokens.
  const focus = 'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-candidate-brand-ui';
  const primary = `inline-flex min-h-11 items-center justify-center rounded-lg bg-candidate-cta px-6 font-semibold text-candidate-cta-foreground disabled:opacity-60 ${focus}`;
  const secondary = `inline-flex min-h-11 items-center justify-center rounded-lg border border-candidate-input-border bg-candidate-surface px-5 font-semibold text-candidate-foreground ${focus}`;

  const stepName = (current: Step | undefined): string =>
    !current ? 'Questions' : current.kind === 'details' ? 'Your details' : current.kind === 'review' ? 'Review and send' : (current.page.name ?? 'Questions');
</script>

<!-- The candidate theme: the careers site's background, ink and 15px base, as an inset in its panel. Native controls only,
     as on the real page. -->
<div class="candidate-preview flex flex-col gap-6 rounded-lg bg-candidate-background p-4 text-candidate-body text-candidate-foreground">
  {#if step}
    <section aria-label={step.kind === 'page' && !step.page.name ? `Questions, page ${step.number}` : stepName(step)} class="flex flex-col gap-4" data-preview-step={step.kind} data-preview-page={step.kind === 'page' ? '' : undefined}>
      {#if steps.length > 1}<PageProgress step={at + 1} steps={steps.length} />{/if}
      <h3 bind:this={heading} tabindex="-1" class="font-display text-heading font-bold focus-visible:outline-none">{stepName(step)}</h3>
      {#if step.kind === 'details'}
        <TextField name="preview-firstName" label="First name" required autocomplete="off" bind:value={firstName} />
        <TextField name="preview-lastName" label="Last name" autocomplete="off" bind:value={lastName} />
        <TextField name="preview-email" label="Email" type="email" required autocomplete="off" bind:value={email} />
        <TextField name="preview-phone" label="Phone" type="tel" autocomplete="off" bind:value={phone} />
        {#if byId.get(RESUME_QUESTION_ID)}
          {@const resume = byId.get(RESUME_QUESTION_ID)!}
          <QuestionField question={resume} bind:answer={answers[resume.id]} />
        {/if}
      {:else if step.kind === 'page'}
        {#each step.page.questionIds as id (id)}
          {@const question = byId.get(id)}
          {#if question}<QuestionField {question} bind:answer={answers[question.id]} />{/if}
        {/each}
      {:else}
        <p class="text-candidate-muted-foreground">The candidate checks their answers here, then sends them.</p>
        {#if aiUse}
          <!-- The review step's last question, asked on every application (apply-flow.md); optional only where AI is allowed outright. -->
          <fieldset data-preview-ai-use class="flex flex-col gap-2">
            <legend class="mb-2 font-display text-heading font-bold">
              Did you use AI tools for this application?{#if aiUse === 'Allowed'}{' '}<span class="font-medium text-candidate-muted-foreground">(optional)</span>{/if}
            </legend>
            <label class="flex items-center gap-2"><input type="radio" name="preview-ai-use" value="DidNotUse" />No, I did not use AI tools</label>
            <label class="flex items-center gap-2"><input type="radio" name="preview-ai-use" value="Used" />Yes, I used AI tools</label>
          </fieldset>
        {/if}
      {/if}
    </section>
  {/if}

  <!-- Native buttons in the candidate theme, as the careers page draws them. Back comes first: the preview has no form to
       submit, so the reading order follows the screen. -->
  <div class="flex flex-wrap items-center gap-3 border-t border-candidate-border pt-4" data-preview-nav>
    {#if at > 0}
      <button type="button" class={secondary} onclick={() => go(at - 1)}>Back</button>
    {/if}
    {#if last && onsend}
      <button type="button" class={primary} onclick={() => onsend($state.snapshot(answers))}>{followUp ? 'Send answers' : 'Send application'}</button>
    {:else if last}
      <button type="button" class={primary} disabled aria-describedby="preview-nothing-sent">{followUp ? 'Send answers' : 'Send application'}</button>
      <!-- Preview adds no sentence about itself (A8): the reason the button does nothing is for screen readers only. -->
      <p id="preview-nothing-sent" class="sr-only">Nothing is sent from a preview.</p>
    {:else}
      <button type="button" class={primary} onclick={() => go(at + 1)}>Next</button>
    {/if}
  </div>
</div>
