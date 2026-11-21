<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import CopyIcon from '~icons/lucide/copy';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';

  // A secret the API shows once, in a read-only field with Copy: `SecretDialog` and the Create key sheet's last step. The
  // value lives only in the caller's state; this component keeps nothing.
  let {
    label,
    value,
    focus = false,
  }: {
    /** The field's label, such as "API key". */
    label: string;
    value: string;
    /** Takes focus when it appears, selected for a copy: a sheet's step that replaced the control holding focus. */
    focus?: boolean;
  } = $props();

  const id = $props.id();
  let copied = $state(false);

  $effect(() => {
    if (focus) document.getElementById(id)?.focus();
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
