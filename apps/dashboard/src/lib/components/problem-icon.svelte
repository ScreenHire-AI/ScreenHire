<script lang="ts">
  import CircleAlertIcon from '@lucide/svelte/icons/circle-alert';
  import { cn } from '#lib/utils.js';

  // The icon before a problem in words: a failed action, a form error, a builder's problem to fix. It sits in the verify
  // colour beside the words, so colour is never the only signal (T1). It is not a status icon: those appear only in
  // status badges (I2), and the warning triangle is kept for the integrity cards (section 14).
  let { class: className }: { class?: string } = $props();
</script>

<CircleAlertIcon aria-hidden="true" data-problem-icon class={cn('size-4 shrink-0 text-status-verify-mark', className)} />
