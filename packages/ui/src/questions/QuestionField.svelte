<script lang="ts">
  // One screening question, rendered from its definition with native controls: what the candidate sees on the careers
  // apply page and what the dashboard's questionnaire preview shows. The careers page renders it on the server with no
  // JavaScript, except file questions, which it hydrates as FileField islands for direct-to-storage uploads.
  import ChoiceField from './ChoiceField.svelte';
  import FileField from './FileField.svelte';
  import { choicesFor, DEFAULT_MAX_FILE_BYTES, fields, fileHintFor, hintFor, isChoice, selectedOf, type AnswerValue, type QuestionSpec } from './model';
  import TextField from './TextField.svelte';

  interface Props {
    question: QuestionSpec;
    /** The answer so far: text, `yes`/`no`, an option id, or option ids for multiple choice. */
    answer?: AnswerValue;
    error?: string | null;
    /** For file questions: the largest file the form takes. */
    maxBytes?: number;
  }

  let { question, answer = $bindable(), error = null, maxBytes = DEFAULT_MAX_FILE_BYTES }: Props = $props();

  const name = $derived(fields.answer(question.id));
  const required = $derived(question.required ?? false);
  const getText = () => (typeof answer === 'string' ? answer : '');
  const setText = (value: string) => (answer = value);
  const getSelected = () => selectedOf(answer);
  const setSelected = (selected: string[]) => (answer = question.type === 'MultiChoice' ? selected : selected[0]);
</script>

{#if question.type === 'File'}
  <FileField
    questionId={question.id}
    label={question.label}
    {required}
    hint={fileHintFor(question, maxBytes)}
    accept={[...(question.acceptedFileTypes ?? [])]}
    {maxBytes}
    {error}
  />
{:else if isChoice(question)}
  <ChoiceField
    {name}
    legend={question.label}
    hint={hintFor(question)}
    {required}
    {error}
    bind:selected={getSelected, setSelected}
    multiple={question.type === 'MultiChoice'}
    options={choicesFor(question)}
  />
{:else}
  <TextField
    {name}
    label={question.label}
    hint={hintFor(question)}
    bind:value={getText, setText}
    {error}
    {required}
    type={question.type === 'Url' ? 'url' : 'text'}
    inputmode={question.type === 'Number' ? 'decimal' : question.type === 'Url' ? 'url' : undefined}
    maxlength={question.maxLength}
    multiline={question.type === 'LongText'}
  />
{/if}
