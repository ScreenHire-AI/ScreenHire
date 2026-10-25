<script lang="ts">
  import type { Question } from '@screenhire/api-client';
  import { Input } from '#lib/components/ui/input/index.js';
  import { checkboxClass, insetClass, labelClass, selectClass } from './controls.js';

  // What an open screening question asks beyond its text, help and options, by its answer kind (the form builder's
  // `settings`): a yes-or-no question's knockout, a number's range, a text answer's longest length and a file's types.
  let { question }: { question: Question } = $props();

  const numberOrNull = (value: string) => (value.trim() === '' ? null : Number(value));
</script>

<div class="@container flex flex-col gap-3">
  {#if question.type === 'YesNo'}
    <div class={insetClass}>
      <label class="flex items-center gap-2 font-semibold pointer-coarse:min-h-11">
        <input
          type="checkbox"
          class={checkboxClass}
          bind:checked={() => !!question.knockout, (on) => (question.knockout = on ? { disqualifyingAnswer: false, reason: null } : null)}
        />
        Knockout question
      </label>
      <p class="text-muted-foreground">A knockout is an objective eligibility check only, such as the right to work, never AI use or an accommodation.</p>
      {#if question.knockout}
        <div class="grid gap-2 @md:grid-cols-[minmax(8rem,12rem)_minmax(0,1fr)]">
          <label class={labelClass}>
            Ineligible answer
            <select
              class={selectClass}
              value={question.knockout.disqualifyingAnswer ? 'yes' : 'no'}
              onchange={(event) => (question.knockout!.disqualifyingAnswer = event.currentTarget.value === 'yes')}
            >
              <option value="no">No</option>
              <option value="yes">Yes</option>
            </select>
          </label>
          <label class={labelClass}>
            Reason the candidate sees
            <Input bind:value={() => question.knockout?.reason ?? '', (value) => (question.knockout!.reason = value || null)} maxlength={500} />
          </label>
        </div>
      {/if}
    </div>
  {:else if question.type === 'Number'}
    <div class="grid gap-2 @md:grid-cols-2">
      <label class={labelClass}>
        Minimum (optional)
        <Input type="number" value={question.min ?? ''} onchange={(event) => (question.min = numberOrNull(event.currentTarget.value))} />
      </label>
      <label class={labelClass}>
        Maximum (optional)
        <Input type="number" value={question.max ?? ''} onchange={(event) => (question.max = numberOrNull(event.currentTarget.value))} />
      </label>
    </div>
  {:else if question.type === 'File'}
    <label class={labelClass}>
      Accepted file types, separated by commas
      <Input
        value={(question.acceptedFileTypes ?? []).join(', ')}
        onchange={(event) =>
          (question.acceptedFileTypes = event.currentTarget.value
            .split(',')
            .map((type) => type.trim().toLowerCase())
            .filter(Boolean))}
      />
    </label>
  {:else if question.type === 'ShortText' || question.type === 'LongText' || question.type === 'Url'}
    <label class={[labelClass, 'max-w-48']}>
      Longest answer (optional)
      <Input type="number" min={1} value={question.maxLength ?? ''} onchange={(event) => (question.maxLength = numberOrNull(event.currentTarget.value))} />
    </label>
  {/if}
</div>
