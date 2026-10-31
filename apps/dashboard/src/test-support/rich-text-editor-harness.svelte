<script lang="ts">
  import RichTextEditor from '#lib/components/rich-text-editor.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import type { RichTextVariable } from '#lib/rich-text.js';

  // The rich text editor under a label, as a form mounts it. `changes` counts what the editor wrote to `value`, and
  // `replace` sets it from outside, as Start from does in the composer.
  let {
    value: initial = '',
    variables,
    editable = true,
    onvalue,
  }: { value?: string; variables?: readonly RichTextVariable[]; editable?: boolean; onvalue?: (value: string) => void } = $props();

  // svelte-ignore state_referenced_locally
  let value = $state(initial);
  let editor = $state<ReturnType<typeof RichTextEditor> | null>(null);

  $effect(() => {
    onvalue?.(value);
  });
</script>

<Tooltip.Provider>
  <span id="message-label">Message</span>
  <RichTextEditor bind:this={editor} bind:value id="message" labelledBy="message-label" {variables} {editable} />
  <button type="button" onclick={() => editor?.insertVariable('firstName')}>Insert first name</button>
  <button type="button" onclick={() => (value = '<p>From a template</p>')}>Start from a template</button>
</Tooltip.Provider>
