<script lang="ts">
  import type { Automation, Knockout, Question } from '@screenhire/api-client';
  import InfoButton from '#lib/components/info-button.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Label } from '#lib/components/ui/label/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import type { MustHaveState } from '#lib/must-have.js';
  import { cn } from '#lib/utils.js';
  import { answerLengthLimit } from '#lib/workflow.js';
  import { insetClass, labelClass, selectClass } from './controls.js';

  // What an open screening question asks beyond its text, help and options, by its answer kind (the form builder's
  // `settings`): a yes-or-no question's must-have answer, a number's range, a text answer's maximum length and a file's
  // types. A must-have is an objective requirement only, which its Info button says (workflow-engine.md section 7). What
  // happens to the answer that fails it is an automation: `mustHave` says which, and the line links to it (A11). The editor
  // that mounts this sets the must-have through `onmusthave`, so it can keep that automation in step.
  let {
    question,
    mustHave,
    left = [],
    automationsHref = '?tab=automations',
    automationHref,
    onshow,
    onmusthave = (knockout) => (question.knockout = knockout),
  }: {
    question: Question;
    /** What happens to the answer that fails the must-have; left out where no automation can read it (a library form). */
    mustHave?: MustHaveState;
    /** The automations that still act on the answer of a question that is not a must-have (`leftReading`). */
    left?: readonly Automation[];
    automationsHref?: string;
    /** Where one automation opens on the Automations tab. */
    automationHref?: (automationId: string) => string;
    /** Opens an automation in place, for a plain click on its link. */
    onshow?: (automationId: string) => void;
    onmusthave?: (knockout: Knockout | null) => void;
  } = $props();

  const uid = $props.id();
  const numberOrNull = (value: string) => (value.trim() === '' ? null : Number(value));
  const lengthLimit = $derived(answerLengthLimit(question.type));
  const linkClass = 'underline underline-offset-2 hover:text-primary';

  // Escape closes the Info button's words first; the open question takes the next one (`FormBuilder`'s row).
  function keepOpen(event: KeyboardEvent) {
    if (event.key === 'Escape' && (event.target as Element | null)?.closest('[data-state="open"]')) event.preventDefault();
  }

  // The button goes with the length it removes, so the focus returns to the open question's text.
  function removeLength(event: MouseEvent) {
    const text = (event.currentTarget as Element).closest('[data-question-row]')?.querySelector<HTMLElement>('[data-question-text]');
    question.maxLength = null;
    text?.focus();
  }

  function show(event: MouseEvent, automation: Automation) {
    if (!onshow || event.metaKey || event.ctrlKey || event.shiftKey) return;
    event.preventDefault();
    onshow(automation.id);
  }
</script>

{#snippet link(automation: Automation, words: string)}<a
    href={automationHref?.(automation.id) ?? automationsHref}
    class={[linkClass, 'break-words']}
    onclick={(event) => show(event, automation)}>{words}</a
  >{/snippet}

{#snippet names(automations: readonly Automation[])}{automations.length === 1 ? 'The automation' : 'The automations'}{' '}{#each automations as automation, index (automation.id)}{index > 0
      ? ', '
      : ''}{@render link(automation, automation.name || 'Untitled automation')}{/each}{/snippet}

<!-- A choice question has nothing more to set; no empty box, so the open question's gaps stay even. -->
{#if question.type !== 'SingleChoice' && question.type !== 'MultiChoice' && (question.type !== 'Url' || question.maxLength != null)}
  <div class="@container flex flex-col gap-3">
    {#if question.type === 'YesNo'}
      <div class={insetClass}>
        <div class="flex items-center gap-2 pointer-coarse:min-h-11">
          <Switch
            id={`${uid}-must-have`}
            checked={!!question.knockout}
            onCheckedChange={(on) => onmusthave(on ? { disqualifyingAnswer: false, reason: null } : null)}
          />
          <Label for={`${uid}-must-have`} class="font-semibold">Must-have answer</Label>
          <!-- svelte-ignore a11y_no_static_element_interactions -->
          <span class="contents" onkeydown={keepOpen}>
            <InfoButton
              label="About must-have answers"
              text="A must-have answer is an objective requirement only, such as the right to work or a licence. Never AI use or an accommodation."
            />
          </span>
        </div>
        {#if question.knockout}
          <p data-must-have-line>
            Candidates who answer
            <select
              class={cn(selectClass, 'mx-0.5 my-0.5 inline-block w-auto align-middle')}
              aria-label="Answer that fails the must-have"
              value={question.knockout.disqualifyingAnswer ? 'yes' : 'no'}
              onchange={(event) => onmusthave({ ...question.knockout, disqualifyingAnswer: event.currentTarget.value === 'yes' })}
            >
              <option value="no">No</option>
              <option value="yes">Yes</option>
            </select>
            {#if mustHave?.kind === 'review'}
              go to Rejection review, where a person decides.
              {#if mustHave.untouched}{@render link(mustHave.automation, 'Change what happens')}{:else}{@render names([mustHave.automation])} does this.{/if}
            {:else}
              do not meet this must-have.
              {#if mustHave?.kind === 'read'}
                {@render names(mustHave.automations)} {mustHave.automations.length === 1 ? 'reads' : 'read'} the answer.
              {:else if mustHave?.kind === 'off'}
                Nothing happens to this answer yet. {@render names(mustHave.automations)} {mustHave.automations.length === 1 ? 'is' : 'are'} off.
              {:else if mustHave?.kind === 'nothing'}
                Nothing happens to this answer yet. <a href={automationsHref} class={linkClass}>Add a step in Automations</a>.
              {/if}
            {/if}
          </p>
        {:else if left.length > 0}
          <p data-must-have-line>{@render names(left)} still {left.length === 1 ? 'reads' : 'read'} this answer.</p>
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
      <div class="flex flex-col gap-1">
        <label class={labelClass} for={`${uid}-file-types`}>File types</label>
        <Input
          id={`${uid}-file-types`}
          aria-describedby={`${uid}-file-types-hint`}
          value={(question.acceptedFileTypes ?? []).join(', ')}
          onchange={(event) =>
            (question.acceptedFileTypes = event.currentTarget.value
              .split(',')
              .map((type) => type.trim().toLowerCase())
              .filter(Boolean))}
        />
        <p id={`${uid}-file-types-hint`} class="text-muted-foreground">Separated by commas, such as .pdf, .docx.</p>
      </div>
    {:else if lengthLimit}
      <div class="flex flex-col gap-1">
        <label class={labelClass} for={`${uid}-length`}>Maximum length</label>
        <span class="flex items-center gap-2">
          <Input
            id={`${uid}-length`}
            type="number"
            min={1}
            max={lengthLimit}
            step={1}
            placeholder={lengthLimit.toLocaleString('en')}
            aria-describedby={`${uid}-length-unit`}
            value={question.maxLength ?? ''}
            onchange={(event) => (question.maxLength = numberOrNull(event.currentTarget.value))}
            class="max-w-28"
          />
          <span id={`${uid}-length-unit`} class="text-muted-foreground">characters</span>
        </span>
      </div>
    {:else if question.type === 'Url'}
      <!-- A link takes no maximum length; one saved with one (by an earlier version, or the API) can only drop it. -->
      <Button variant="outline" size="sm" class="self-start" onclick={removeLength}>Remove maximum length</Button>
    {/if}
  </div>
{/if}
