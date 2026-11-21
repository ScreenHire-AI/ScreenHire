<script lang="ts">
  import SecretField from '#lib/components/secret-field.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';

  // A secret the API shows once after an action on a page (a new webhook endpoint, a rotated signing secret): its value
  // in a read-only field with Copy, and Done. The value lives only in the caller's state and is gone when the dialog
  // closes; it never reaches the query cache, the address or storage. A secret made in a sheet shows in that sheet
  // instead (Create key), through the same `SecretField`.
  let {
    open = $bindable(false),
    title,
    label,
    value,
    onclose,
  }: {
    open?: boolean;
    title: string;
    /** The field's label, such as "Signing secret". */
    label: string;
    value: string;
    /** Runs when the dialog closes, by Done, Escape or the close button; the caller forgets the value here. */
    onclose?: () => void;
  } = $props();
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
    <SecretField {label} {value} />
    <Dialog.Footer>
      <Button size="lg" onclick={() => ((open = false), onclose?.())}>Done</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
