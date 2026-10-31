<script lang="ts">
  import CheckIcon from '@lucide/svelte/icons/check';
  import CopyIcon from '@lucide/svelte/icons/copy';
  import { Button } from '#lib/components/ui/button/index.js';

  // Code for people to paste elsewhere (design system section 11, Code block): the embed snippet on Settings,
  // Organization, an MCP client's config and the example request on Settings, API keys. The code in monospace, then
  // Copy. Without a clipboard (an insecure origin, a refused permission) Copy selects the code for a manual copy.
  let { code, label }: { code: string; /** Names the code for screen readers and the copy's status line. */ label: string } = $props();

  let pre: HTMLPreElement | undefined = $state();
  let copied = $state(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(code);
      copied = true;
    } catch {
      if (!pre) return;
      const range = document.createRange();
      range.selectNodeContents(pre);
      const selection = window.getSelection();
      selection?.removeAllRanges();
      selection?.addRange(range);
    }
  }
</script>

<div class="flex min-w-0 flex-col items-start gap-2">
  <!-- A role, so screen readers announce the label; lines break at spaces first, and inside a long address only when it
       must (overflow-wrap: anywhere keeps the block from widening its panel). -->
  <pre bind:this={pre} role="group" aria-label={label} class="w-full rounded-lg bg-secondary px-3.5 py-3 font-mono text-code whitespace-pre-wrap [overflow-wrap:anywhere]">{code}</pre>
  <Button variant="outline" size="lg" onclick={copy}>
    {#if copied}<CheckIcon aria-hidden="true" />Copied{:else}<CopyIcon aria-hidden="true" />Copy{/if}
  </Button>
  <p role="status" class="sr-only">{copied ? `${label} copied` : ''}</p>
</div>
