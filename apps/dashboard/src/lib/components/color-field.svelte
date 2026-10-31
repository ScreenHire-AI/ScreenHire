<script lang="ts">
  import type { ColorContrast } from '@screenhire/api-client';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { brandColorPattern, normalizeBrandColor } from '#lib/branding.js';

  // A colour field (design system section 11): the native colour picker beside a hex field, empty for ScreenHire's
  // default. Under it, each pair the colour fails (WCAG 2.2 AA, checked on the server as it is typed) and, on the same
  // row, the nearest shade that passes as an outline button (F6). `errors` are a refused save's, which take the place
  // of the live check.
  let {
    id,
    label,
    description,
    value = $bindable(''),
    check,
    errors,
    fallback,
    placeholder = "ScreenHire's",
  }: {
    id: string;
    label: string;
    description: string;
    value?: string;
    /** The live contrast check of the colour in the field, once it is a whole colour. */
    check?: ColorContrast | null;
    errors?: string[];
    /** What the picker shows while the field is empty: the default this colour falls back to. */
    fallback: string;
    /** What the empty field says it means. */
    placeholder?: string;
  } = $props();

  const whole = $derived(brandColorPattern.test(value.trim()));
  const picked = $derived(whole ? normalizeBrandColor(value) : fallback);
  // The check answers for the colour it was asked about; a stale one says nothing.
  const current = $derived(whole && check && check.color === normalizeBrandColor(value) ? check : null);
  const problems = $derived(errors?.length ? errors : current && !current.passes ? current.problems : []);
  const suggestion = $derived(current && !current.passes ? current.suggestion : null);
</script>

<Field.Field data-invalid={problems.length > 0 ? true : undefined}>
  <Field.Label for={id}>{label}</Field.Label>
  <div class="flex min-w-0 gap-2">
    <input
      type="color"
      aria-label={`Pick the ${label.toLowerCase()}`}
      value={picked}
      oninput={(event) => (value = event.currentTarget.value)}
      class="h-9 w-12 shrink-0 cursor-pointer rounded-lg border border-input bg-card p-1 pointer-coarse:h-11 pointer-coarse:w-14"
    />
    <Input
      {id}
      name={id}
      maxlength={7}
      autocomplete="off"
      spellcheck={false}
      {placeholder}
      class="min-w-0 flex-1 font-mono"
      bind:value
      aria-invalid={problems.length > 0 ? 'true' : undefined}
      aria-describedby={[`${id}-hint`, problems.length > 0 && `${id}-error`].filter(Boolean).join(' ')}
    />
  </div>
  <Field.Description id={`${id}-hint`}>{description}</Field.Description>
  {#if problems.length > 0}
    <div class="flex min-w-0 flex-wrap items-start justify-between gap-2">
      <Field.Error id={`${id}-error`} class="min-w-0 flex-1" errors={problems.map((message) => ({ message }))} />
      {#if suggestion}
        <Button variant="outline" class="shrink-0" onclick={() => (value = suggestion)}>Use {suggestion}</Button>
      {/if}
    </div>
  {/if}
</Field.Field>
