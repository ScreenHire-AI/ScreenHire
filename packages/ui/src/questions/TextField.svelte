<script lang="ts">
  // A labelled text input or textarea with native validation attributes. Server-rendered on the careers apply page (no
  // JavaScript), and live in the dashboard's questionnaire preview, where `value` binds.
  import { describedBy, fieldId } from './model';

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
  }: Props = $props();

  const id = $derived(fieldId(name));
  const described = $derived(describedBy(id, hint, error));
</script>

<div class="flex flex-col gap-1.5">
  <label for={id} class="text-sm font-bold">
    {label}
    {#if !required}<span class="font-medium text-candidate-muted-foreground"> (optional)</span>{/if}
  </label>
  {#if hint}<p id={`${id}-hint`} class="text-[13px] text-candidate-muted-foreground">{hint}</p>{/if}
  {#if error}<p id={`${id}-error`} class="text-sm font-semibold text-destructive">{error}</p>{/if}
  {#if multiline}
    <textarea
      {id}
      {name}
      {required}
      maxlength={maxlength ?? undefined}
      aria-invalid={error ? 'true' : undefined}
      aria-describedby={described}
      class="field-input"
      bind:value
    ></textarea>
  {:else}
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
      bind:value
    />
  {/if}
</div>
