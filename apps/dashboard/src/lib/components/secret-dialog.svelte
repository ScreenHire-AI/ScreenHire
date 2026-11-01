<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import CopyIcon from '~icons/lucide/copy';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';

  // A secret the API shows once (an API key, a webhook signing secret): its value in a read-only field with Copy, and
  // Done. The value lives only in the caller's state and is gone when the dialog closes; it never reaches the query
  // cache, the address or storage.
  let {
    open = $bindable(false),
    title,
    label,
    value,
    onclose,
  }: {
    open?: boolean;
    title: string;
    /** The field's label, such as "API key". */
    label: string;
    value: string;
    /** Runs when the dialog closes, by Done, Escape or the close button; the caller forgets the value here. */
    onclose?: () => void;
  } = $props();

  const id = $props.id();
  let copied = $state(false);

  $effect(() => {
    if (open) copied = false;
  });

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      copied = true;
    } catch {
      // No clipboard (an insecure origin, a refused permission): the field stays selected for a manual copy.
      document.getElementById(id)?.focus();
    }
  }
</script>

<Dialog.Root
  bind:open
  onOpenChange={(next) => {
    if (!next) onclose?.();
  }}
>
  <Dialog.Content class="sm:max-w-lg">
    <Dialog.Header>
      <Dialog.Title>{title}</Dialog.Title>
      <Dialog.Description>Copy it now. You will not see it again.</Dialog.Description>
    </Dialog.Header>
    <Field.Field>
      <Field.Label for={id}>{label}</Field.Label>
      <div class="flex min-w-0 gap-2">
        <Input {id} readonly {value} class="min-w-0 flex-1 font-mono" onfocus={(event) => event.currentTarget.select()} />
        <Button variant="outline" size="lg" onclick={copy}>
          {#if copied}<CheckIcon aria-hidden="true" />Copied{:else}<CopyIcon aria-hidden="true" />Copy{/if}
        </Button>
      </div>
    </Field.Field>
    <p role="status" class="sr-only">{copied ? `${label} copied` : ''}</p>
    <Dialog.Footer>
      <Button size="lg" onclick={() => ((open = false), onclose?.())}>Done</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
