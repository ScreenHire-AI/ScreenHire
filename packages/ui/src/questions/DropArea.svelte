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
    chooseLabel?: string;
    /** The word on the area once a file is chosen; a click or drop still replaces it. */
    replaceLabel?: string;
    removeLabel?: string;
    /** The file as it will be used, above the words and part of the target (the image field's picture). */
    preview?: Snippet;
    /**
     * Whether the input keeps the file. A field that posts with its form keeps it (the default); one whose owner sends
     * the bytes itself says false, and the input is cleared once `onfile` has the file, so picking it again still fires.
     */
    keep?: boolean;
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
    required,
    disabled,
    invalid,
    describedby,
    file,
    status,
    chooseLabel = 'Choose file',
    replaceLabel = 'Replace file',
    removeLabel = 'Remove',
    preview,
    keep = true,
    onfile,
    onproblem,
    onremove,
  }: Props = $props();

  // Before the page's JavaScript runs the input is shown as the browser draws it, inside the same frame.
  let mounted = $state(false);
  let input = $state<HTMLInputElement>();
  let over = $state(false);
  // dragenter and dragleave fire again for every child the pointer crosses, so the area counts them.
  let depth = 0;

  $effect(() => {
    mounted = true;
    // A file chosen before the page hydrated fired its change event too early: take it now.
    const early = input?.files?.[0];
    if (early) untrack(() => take(early, false));
  });

  function clear() {
    if (input) input.value = '';
  }

  // Puts a dropped file into the input, so a field that posts with its form posts it too. A single-file input never
  // gets the whole dropped list, only the first file. False in a browser without the DataTransfer constructor.
  function put(dropped: File): boolean {
    try {
      const transfer = new DataTransfer();
      transfer.items.add(dropped);
      input!.files = transfer.files;
      return true;
    } catch {
      return false;
    }
  }

  // The same checks and the same hand-off for a picked file and a dropped one.
  function take(chosen: File | undefined, dropped: boolean) {
    if (!chosen || disabled) return;
    const problem = fileProblem(chosen, accept, maxBytes);
    if (problem) {
      // A refused pick has already replaced the input's file, so the field must let go of the one it showed. A refused
      // drop never reached the input, and the earlier file stays.
      if (!dropped) {
        clear();
        if (keep && file) onremove?.();
      }
      onproblem?.(problem);
      return;
    }

    // Told it was taken with nothing in its input, a field that posts with its form would post no file.
    if (keep && dropped && !put(chosen)) {
      onproblem?.('This browser cannot take a dropped file. Choose it instead.');
      return;
    }

    onfile(chosen);
    if (!keep) clear();
  }

  // Without cancelling the drag the browser never fires drop. A file's drag is cancelled even while disabled: left alone,
  // a file dropped here opens in the tab and loses the form.
  function ondragover(event: DragEvent) {
    const files = event.dataTransfer?.types.includes('Files');
    if (!files) return false;
    event.preventDefault();
    event.dataTransfer!.dropEffect = disabled ? 'none' : 'copy';
    return !disabled;
  }

  function ondragenter(event: DragEvent) {
    if (!ondragover(event)) return;
    depth += 1;
    over = true;
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

  // The whole area opens the picker, and Remove is handled here too. This is one capture listener because Svelte's
  // delegated click handling would add about 300 bytes to the careers island, whose budget is tight.
  function onclickcapture(event: MouseEvent) {
    const target = event.target as Element;
    // The input's own click passes through here too: leave it alone, or it would open the picker twice.
    if (disabled || target === input) return;
    if (!target.closest('button')) {
      input?.click();
      return;
    }

    clear();
    onremove?.();
    // The Remove button goes with the file: focus moves to the input, or it would fall back to the page.
    input?.focus();
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions, a11y_click_events_have_key_events: a drop and a click on the area have no key press; the file input inside it is focusable and opens the same picker with Enter or Space -->
<div
  data-drop-area
  data-tone={tone}
  data-over={over ? '' : undefined}
  class="drop-area"
  {ondragenter}
  {ondragover}
  {ondragleave}
  {ondrop}
  {onclickcapture}
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
    class="file-input"
  />
  {#if mounted}
    <div class="drop-area-body">
      {#if preview}{@render preview()}{/if}
      <!-- Stacked in a narrow area and in a row in a wide one, so the buttons never wrap (F2, F5). -->
      <div class="drop-area-row">
        <div aria-live="polite" class="flex max-w-full min-w-0 flex-col items-center text-center">
          {#if status}
            <span class="font-semibold">{status}</span>
          {:else if file}
            <span class="max-w-full truncate font-semibold" title={file.name}>{file.name}</span>
            <span class="drop-area-quiet">{describeSize(file.size)}</span>
          {:else}
            <span>Drop a file here or</span>
          {/if}
        </div>
        {#if !status}
          <div class="drop-area-actions">
            <!-- Looks like a button and is not one: the input above is the control, so assistive technology finds one picker. -->
            <span aria-hidden="true" class="drop-area-button">{file ? replaceLabel : chooseLabel}</span>
            {#if file}
              <button type="button" class="drop-area-button">{removeLabel}</button>
            {/if}
          </div>
        {/if}
      </div>
    </div>
  {:else if file}
    <!-- Without JavaScript the input says "No file chosen" (hidden by .file-input[data-uploaded]), so the file is named here. -->
    <p class="mt-2 font-semibold text-candidate-body">{file.name} ({describeSize(file.size)})</p>
  {/if}
</div>
