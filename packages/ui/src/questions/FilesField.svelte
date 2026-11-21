<script lang="ts">
  // The candidate's reply files: several files in one post, drawn by the drop area in its `multiple` mode (DropArea.svelte).
  // Server-rendered, and without JavaScript, it is one named file input that takes several files and posts with the form.
  // Hydrated, each file is a row with its name, size and Remove, and the area keeps the named input holding exactly those
  // files, so the form posts what is shown. A file the area refuses is named in the field's error, one line per file. The
  // careers server checks the count and the bytes again when the form arrives (apps/careers/src/lib/portal.ts).
  import { untrack } from 'svelte';
  import DropArea from './DropArea.svelte';
  import { fieldId } from './model';

  interface Props {
    /** The input's name, which is the form field the server reads the files from. */
    name: string;
    label: string;
    hint: string;
    accept: string[];
    /** The most bytes one file may be. */
    maxBytes: number;
    maxFiles: number;
    /** The most bytes all the files may add up to. */
    maxTotalBytes: number;
  }

  const { name, label, hint, accept, maxBytes, maxFiles, maxTotalBytes }: Props = $props();

  const inputId = untrack(() => fieldId(name));
  const hintId = `${inputId}-hint`;
  const errorId = `${inputId}-error`;

  let problems = $state<string[]>([]);
</script>

<div class="flex flex-col gap-2">
  <label for={inputId} class="text-candidate-body font-bold">
    {label} <span class="font-medium text-candidate-muted-foreground">(optional)</span>
  </label>
  <p id={hintId} class="text-candidate-hint text-candidate-muted-foreground">{hint}</p>
  {#if problems.length > 0}
    <ul id={errorId} class="flex flex-col gap-1 text-candidate-body font-semibold text-destructive">
      {#each problems as problem (problem)}
        <li>{problem}</li>
      {/each}
    </ul>
  {/if}
  <DropArea
    id={inputId}
    {name}
    multiple
    {accept}
    {maxBytes}
    {maxFiles}
    {maxTotalBytes}
    invalid={problems.length > 0}
    describedby={problems.length > 0 ? `${hintId} ${errorId}` : hintId}
    onproblems={(messages) => (problems = messages)}
  />
</div>
