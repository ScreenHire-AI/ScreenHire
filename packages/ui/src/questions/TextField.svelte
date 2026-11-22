<script lang="ts">
  // A labelled text input or textarea with native validation attributes. Server-rendered on the careers apply page (no
  // JavaScript), and live in the dashboard's questionnaire preview, where `value` binds. Its check line shows through CSS
  // (`:user-invalid`, questions.css) once the candidate leaves the field in a state the browser knows is wrong, so the
  // hint comes beside the field with no script; the server's errors stay the accessible path, so the line is not in
  // `aria-describedby` (a hidden description would be read while the field is fine).
  import { checkFor, describedBy, fieldId } from './model';

  interface Props {
    name: string;
    label: string;
    value?: string;
    error?: string | null;
    hint?: string | null;
    required?: boolean;
    type?: 'text' | 'email' | 'tel' | 'url';
    inputmode?: 'text' | 'email' | 'tel' | 'url' | 'decimal';
    autocomplete?: AutoFill;
    maxlength?: number | null;
    multiline?: boolean;
    /** The line shown when the browser finds the field wrong; by default from its type and `required`. */
    check?: string | null;
    /** Takes the focus when the page loads: the first question of a careers page. Never in the dashboard's preview. */
    autofocus?: boolean;
  }

  let {
    name,
    label,
    value = $bindable(''),
    error = null,
    hint = null,
    required = false,
    type = 'text',
    inputmode,
    autocomplete,
    maxlength = null,
    multiline = false,
    check,
    autofocus = false,
  }: Props = $props();

  const id = $derived(fieldId(name));
  const checkLine = $derived(check === undefined ? checkFor(type, required) : check);
  const described = $derived(describedBy(id, hint, error));
</script>

<div class="field flex flex-col gap-2">
  <label for={id} class="text-candidate-body font-semibold">
    {label}
    {#if !required}<span class="font-medium text-candidate-muted-foreground"> (optional)</span>{/if}
  </label>
  {#if hint}<p id={`${id}-hint`} class="text-candidate-hint text-candidate-muted-foreground">{hint}</p>{/if}
  {#if error}<p id={`${id}-error`} class="text-candidate-body font-semibold text-destructive">{error}</p>{/if}
  {#if multiline}
    <!-- svelte-ignore a11y_autofocus -- only the careers page sets it, for the first question of a page the candidate just moved to (apply-flow.md §7); the preview never does -->
    <textarea
      {id}
      {name}
      {required}
      maxlength={maxlength ?? undefined}
      aria-invalid={error ? 'true' : undefined}
      aria-describedby={described}
      class="field-input"
      {autofocus}
      bind:value
    ></textarea>
  {:else}
    <!-- svelte-ignore a11y_autofocus -- only the careers page sets it, for the first question of a page the candidate just moved to (apply-flow.md §7); the preview never does -->
    <input
      {id}
      {name}
      {type}
      {required}
      {inputmode}
      {autocomplete}
      maxlength={maxlength ?? undefined}
      aria-invalid={error ? 'true' : undefined}
      aria-describedby={described}
      class="field-input"
      {autofocus}
      bind:value
    />
  {/if}
  {#if checkLine}<p class="field-check">{checkLine}</p>{/if}
</div>
