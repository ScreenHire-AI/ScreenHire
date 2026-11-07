<script lang="ts">
  // The one place a file is chosen, in the careers apply form and in the recruiter dashboard (design system section 11,
  // Drop area; A12: any file input takes a drop). It wraps a real file input, so the keyboard, screen readers, phones and
  // the browser's own validation keep working. Rendered on the server, before the page's JavaScript runs, it is only that
  // input in its dashed frame, which posts with its form (careers pages work without JavaScript). Once mounted, the input
  // is visually hidden and the whole area is the target: a click anywhere opens the picker, a dropped file is taken, and
  // the area says "Drop a file here or Choose file", or names the chosen file and its size with Remove.
  //
  // The area checks the type and size of every file, picked or dropped, with `fileProblem`, and reports why one is wrong
  // through `onproblem` so the owning field shows the words in its own error. The owner holds the chosen file (`file`)
  // and what to do with its bytes (`onfile`); a field that posts with its form leaves the input as it is, and a dropped
  // file is put into the input first so it posts too. The area sets no cursor and no inline style: the theme gives
  // [data-drop-area] the hand (A10) and careers pages forbid inline styles.
  import { untrack, type Snippet } from 'svelte';
  import { describeSize, fileProblem } from './files';

  type Tone = 'candidate' | 'app';

  interface Props {
    /** The input's id, which the field's label points at. */
    id: string;
    /** The input's name; leave it out when the owner sends the bytes itself. */
    name?: string;
    /** File name endings the field takes (`['.pdf']`); empty takes any type. */
    accept?: readonly string[];
    /** The largest file in bytes; unset takes any size. */
    maxBytes?: number;
    /** Which tokens draw it: the candidate pages' or the recruiter app's. */
    tone?: Tone;
    required?: boolean;
    disabled?: boolean;
    invalid?: boolean;
    describedby?: string;
    /** The file chosen so far, as the owner knows it. */
    file?: { name: string; size: number } | null;
    /** A line in place of the file, such as "Uploading...". */
    status?: string | null;
    /** The words before Choose file. */
    prompt?: string;
    chooseLabel?: string;
    /** The word on the area once a file is chosen; a click or drop still replaces it. */
    replaceLabel?: string;
    removeLabel?: string;
    /** A small picture before the prompt (the dashboard passes a Lucide icon; this package has none). */
    icon?: Snippet;
    /**
     * Whether the input keeps the file. A field that posts with its form keeps it (the default); one whose owner sends
     * the bytes itself says false, and the input is cleared once `onfile` has the file, so picking it again still fires.
     */
    keep?: boolean;
    input?: HTMLInputElement | undefined;
    /** A file that passed the checks, whether it was picked or dropped. */
    onfile: (file: File) => void;
    /** Why a file was refused, in words for the person. */
    onproblem?: (message: string) => void;
    /** Remove was pressed, or the picker was closed with its selection cleared. */
    onremove?: () => void;
  }

  let {
    id,
    name,
    accept = [],
    maxBytes,
    tone = 'candidate',
    required = false,
    disabled = false,
    invalid = false,
    describedby,
    file = null,
    status = null,
    prompt = 'Drop a file here or',
    chooseLabel = 'Choose file',
    replaceLabel = 'Replace file',
    removeLabel = 'Remove',
    icon,
    keep = true,
    input = $bindable(),
    onfile,
    onproblem,
    onremove,
  }: Props = $props();

  // Full class strings, so Tailwind finds them through questions.css's @source line.
  const looks = {
    candidate: {
      frame:
        'flex min-w-0 flex-wrap items-center justify-center gap-3 rounded-xl border-[1.5px] border-dashed border-candidate-input-border bg-candidate-surface p-3 text-candidate-body text-candidate-foreground has-[input:focus-visible]:outline-2 has-[input:focus-visible]:outline-offset-2 has-[input:focus-visible]:outline-candidate-brand-ui data-[over]:border-solid data-[over]:border-candidate-brand-ui data-[over]:bg-candidate-background',
      button:
        'inline-flex min-h-11 items-center gap-2 rounded-[10px] border border-candidate-input-border bg-candidate-background px-4 font-bold text-candidate-foreground',
      quiet: 'text-candidate-hint text-candidate-muted-foreground',
    },
    app: {
      frame:
        'flex min-w-0 flex-wrap items-center justify-center gap-3 rounded-lg border border-dashed border-input bg-secondary p-3 text-body has-[input:focus-visible]:border-ring has-[input:focus-visible]:ring-3 has-[input:focus-visible]:ring-ring/50 data-[over]:border-solid data-[over]:border-ring data-[over]:bg-accent',
      button:
        'inline-flex h-9 pointer-coarse:h-11 items-center gap-2 rounded-lg border border-input bg-background px-3 font-medium text-foreground',
      quiet: 'text-caption text-muted-foreground',
    },
  } as const;
  const look = $derived(looks[tone]);

  // Before the page's JavaScript runs the input is shown as the browser draws it, inside the same frame.
  let mounted = $state(false);
  let over = $state(false);
  // dragenter and dragleave fire again for every child the pointer crosses, so the area counts them.
  let depth = 0;

  $effect(() => {
    mounted = true;
    // A file chosen before the page hydrated fired its change event too early: take it now.
    const early = input?.files?.[0];
    if (early) untrack(() => take(early, false));
  });

  const hasFiles = (event: DragEvent) => event.dataTransfer?.types.includes('Files') ?? false;

  function clear() {
    if (input) input.value = '';
  }

  // Puts a dropped file into the input, so a field that posts with its form posts it too. A single-file input never
  // gets the whole dropped list, only the first file.
  function put(dropped: File) {
    if (!input) return;
    try {
      const transfer = new DataTransfer();
      transfer.items.add(dropped);
      input.files = transfer.files;
    } catch {
      // A browser without the DataTransfer constructor: the owner still gets the file through onfile.
    }
  }

  // The same checks and the same hand-off for a picked file and a dropped one.
  function take(chosen: File | undefined, dropped: boolean) {
    if (!chosen || disabled) return;
    const problem = fileProblem(chosen, accept, maxBytes);
    if (problem) {
      clear();
      onproblem?.(problem);
      return;
    }

    if (keep && dropped) put(chosen);
    onfile(chosen);
    if (!keep) clear();
  }

  function ondragenter(event: DragEvent) {
    if (disabled || !hasFiles(event)) return;
    event.preventDefault();
    depth += 1;
    over = true;
  }

  function ondragover(event: DragEvent) {
    if (disabled || !hasFiles(event)) return;
    // Without this the browser never fires drop.
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  }

  function ondragleave() {
    depth = Math.max(0, depth - 1);
    if (depth === 0) over = false;
  }

  function ondrop(event: DragEvent) {
    event.preventDefault();
    depth = 0;
    over = false;
    if (disabled) return;
    take(event.dataTransfer?.files[0], true);
  }

  // The whole area opens the picker; the Remove button keeps its own click.
  function onclick(event: MouseEvent) {
    // The input's own click bubbles up here too: leave it alone, or it would open the picker twice.
    if (disabled || event.target === input || (event.target as Element).closest('button')) return;
    input?.click();
  }

  function remove() {
    clear();
    onremove?.();
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events: a drop and a click on the area have no key press; the file input inside it is focusable and opens the same picker with Enter or Space -->
<div
  data-drop-area
  data-tone={tone}
  data-over={mounted && over ? '' : undefined}
  data-filled={file ? '' : undefined}
  data-disabled={disabled ? '' : undefined}
  class={mounted ? look.frame : 'rounded-xl border-[1.5px] border-dashed border-candidate-input-border bg-candidate-surface p-3'}
  {ondragenter}
  {ondragover}
  {ondragleave}
  {ondrop}
  {onclick}
>
  <input
    bind:this={input}
    type="file"
    {id}
    {name}
    accept={accept.join(',') || undefined}
    {required}
    {disabled}
    data-uploaded={file ? '' : undefined}
    aria-invalid={invalid ? 'true' : undefined}
    aria-describedby={describedby}
    onchange={() => {
      const chosen = input?.files?.[0];
      // Closing the picker with nothing chosen clears the input in some browsers: the field must not keep showing a file.
      if (chosen) take(chosen, false);
      else if (file) onremove?.();
    }}
    class={mounted ? 'sr-only' : 'file-input'}
  />
  {#if !mounted && file}
    <!-- Without JavaScript the input says "No file chosen" (hidden by .file-input[data-uploaded]), so the file is named here. -->
    <p class="mt-2 font-semibold text-candidate-body">{file.name} ({describeSize(file.size)})</p>
  {/if}
  {#if mounted}
    {#if icon}<span aria-hidden="true" class={look.quiet}>{@render icon()}</span>{/if}
    <div aria-live="polite" class="flex min-w-0 flex-col items-center text-center">
      {#if status}
        <span class="font-semibold">{status}</span>
      {:else if file}
        <span class="max-w-full truncate font-semibold" title={file.name}>{file.name}</span>
        <span class={look.quiet}>{describeSize(file.size)}</span>
      {:else}
        <span>{prompt}</span>
      {/if}
    </div>
    <!-- Looks like a button and is not one: the input above is the control, so assistive technology finds one picker. -->
    {#if !status}<span aria-hidden="true" class={look.button}>{file ? replaceLabel : chooseLabel}</span>{/if}
    {#if file && !status}
      <button type="button" {disabled} onclick={remove} class={look.button}>{removeLabel}</button>
    {/if}
  {/if}
</div>
