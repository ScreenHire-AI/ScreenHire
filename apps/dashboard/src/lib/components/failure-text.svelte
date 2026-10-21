<script lang="ts">
  import StatusMark from '#lib/components/status-mark.svelte';
  import { cn } from '#lib/utils.js';

  // A failed action or form, in one line where it happened: the verify mark, its colour and the reason (T1). The line is
  // always in the page, empty until there is a message, so screen readers announce it when one arrives. A section that
  // cannot load at all is `QueryError` instead.
  let {
    message,
    role = 'alert',
    class: className,
  }: {
    message: string | null | undefined;
    /** `status` for a line that also announces successes, such as a move. */
    role?: 'alert' | 'status';
    class?: string;
  } = $props();
</script>

<p {role} data-slot="failure-text" class={cn('flex items-center gap-2 text-body text-status-verify empty:hidden', className)}>
  {#if message}<StatusMark shape="square" tone="verify" />{message}{/if}
</p>
