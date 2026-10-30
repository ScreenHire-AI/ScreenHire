<script lang="ts" module>
  import type { Question } from '@screenhire/api-client';

  /** The demo questionnaire, which the pop-out preview page draws until the editor sends its own. */
  export const demoQuestions: Question[] = [
    { id: 'notice', type: 'ShortText', label: 'What is your notice period?', required: true },
    { id: 'right-to-work', type: 'YesNo', label: 'Do you have the right to work in the country this job is in?', required: true },
  ];
</script>

<script lang="ts">
  import EditPreviewLayout from '#lib/components/edit-preview-layout.svelte';
  import Panel from '#lib/components/panel.svelte';
  import PreviewFrame from '#lib/components/preview-frame.svelte';
  import QuestionnairePreview from '#lib/components/workflow/questionnaire-preview.svelte';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';

  // An editor whose result a candidate sees (A8): side by side where both fit, else Edit and Preview swap in place; the
  // window icon opens the preview in a second window that follows each edit. Nothing is saved.
  let mode = $state<'edit' | 'preview'>('edit');
  let questions = $state(demoQuestions.map((question) => ({ ...question })));
  const source = createPreviewSource(() => questions);
</script>

<EditPreviewLayout bind:mode onpopout={() => source.open('/style-guide/preview')}>
  {#snippet editor()}
    <Panel title="Questions">
      <div class="flex flex-col gap-4">
        {#each questions as question, index (question.id)}
          <Field.Field>
            <Field.Label for={`style-guide-question-${question.id}`}>Question {index + 1}</Field.Label>
            <Input id={`style-guide-question-${question.id}`} bind:value={question.label} />
          </Field.Field>
        {/each}
      </div>
    </Panel>
  {/snippet}
  {#snippet preview(popout)}
    <Panel title="Preview" actions={popout}>
      <PreviewFrame>
        <QuestionnairePreview {questions} followUp />
      </PreviewFrame>
    </Panel>
  {/snippet}
</EditPreviewLayout>
