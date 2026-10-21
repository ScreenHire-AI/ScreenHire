<script lang="ts">
  import type { Question } from '@screenhire/api-client';
  import { formQuestions, QuestionField, TextField, type AnswerValue } from '@screenhire/ui/questions';

  // The candidate's form, rendered by the same components the careers apply page renders (packages/ui), in the
  // candidate theme. The answers are the recruiter's to try out; nothing is sent anywhere.
  let { questions }: { questions: readonly Question[] } = $props();

  let answers = $state<Record<string, AnswerValue | undefined>>({});
  let fullName = $state('');
  let email = $state('');
  let phone = $state('');
</script>

<!-- The candidate theme: the careers site's background, ink and 15px base, as an inset in its panel. Native controls only,
     as on the real page. -->
<div class="candidate-preview flex flex-col gap-4 rounded-lg bg-candidate-background p-4 text-candidate-body text-candidate-foreground">
  <p class="text-caption text-candidate-chip-foreground">Your details</p>
  <TextField name="preview-fullName" label="Full name" required autocomplete="off" bind:value={fullName} />
  <TextField name="preview-email" label="Email" type="email" required autocomplete="off" bind:value={email} />
  <TextField name="preview-phone" label="Phone" type="tel" autocomplete="off" bind:value={phone} />
  {#each formQuestions(questions) as question (question.id)}
    <QuestionField {question} bind:answer={answers[question.id]} />
  {/each}
</div>
