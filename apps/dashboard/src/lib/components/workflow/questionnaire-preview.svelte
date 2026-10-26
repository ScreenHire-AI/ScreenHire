<script lang="ts">
  import type { Question } from '@screenhire/api-client';
  import { formQuestions, QuestionField, TextField, type AnswerValue } from '@screenhire/ui/questions';

  // The candidate's form, rendered by the same components the careers pages render (packages/ui), in the candidate
  // theme. The apply form starts with the candidate's details and the resume; a follow-up questionnaire (`followUp`) is
  // its questions alone, as its page shows them. The answers are the recruiter's to try out; nothing is sent anywhere.
  let { questions, followUp = false }: { questions: readonly Question[]; followUp?: boolean } = $props();

  let answers = $state<Record<string, AnswerValue | undefined>>({});
  let firstName = $state('');
  let lastName = $state('');
  let email = $state('');
  let phone = $state('');
</script>

<!-- The candidate theme: the careers site's background, ink and 15px base, as an inset in its panel. Native controls only,
     as on the real page. -->
<div class="candidate-preview flex flex-col gap-4 rounded-lg bg-candidate-background p-4 text-candidate-body text-candidate-foreground">
  {#if !followUp}
    <p class="text-caption text-candidate-chip-foreground">Your details</p>
    <TextField name="preview-firstName" label="First name" required autocomplete="off" bind:value={firstName} />
    <TextField name="preview-lastName" label="Last name" autocomplete="off" bind:value={lastName} />
    <TextField name="preview-email" label="Email" type="email" required autocomplete="off" bind:value={email} />
    <TextField name="preview-phone" label="Phone" type="tel" autocomplete="off" bind:value={phone} />
  {/if}
  {#each followUp ? questions : formQuestions(questions) as question (question.id)}
    <QuestionField {question} bind:answer={answers[question.id]} />
  {/each}
</div>
