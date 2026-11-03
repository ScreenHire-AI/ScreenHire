<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import CopyIcon from '~icons/lucide/copy';
  import IconButton from '#lib/components/icon-button.svelte';

  // Copies one value, such as an email address or a phone number, to the clipboard: a quiet icon button (A4) that shows a
  // check for two seconds and says "Copied" to screen readers. Without a clipboard (an insecure origin, a refused
  // permission) it says the copy failed, and the value stays on screen beside it to copy by hand.
  let { value, label }: { value: string; /** What it copies, as its name and tooltip: "Copy email address". */ label: string } = $props();

  let status = $state<'idle' | 'copied' | 'failed'>('idle');
  let timer: ReturnType<typeof setTimeout> | undefined;

  async function copy() {
    clearTimeout(timer);
    try {
      await navigator.clipboard.writeText(value);
      status = 'copied';
    } catch {
      status = 'failed';
    }
    timer = setTimeout(() => (status = 'idle'), 2000);
  }

  $effect(() => () => clearTimeout(timer));
</script>

<IconButton {label} size="icon-xs" onclick={copy}>
  {#if status === 'copied'}<CheckIcon aria-hidden="true" />{:else}<CopyIcon aria-hidden="true" />{/if}
</IconButton>
<span role="status" class="sr-only">{status === 'copied' ? 'Copied' : status === 'failed' ? 'Could not copy' : ''}</span>
