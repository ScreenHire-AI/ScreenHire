<script lang="ts">
  import type { SendingDomainMark } from '#lib/sending-domain.js';
  import { statuses } from '#lib/status.js';

  // A setting's state as a bare mark after its name in Settings (design system section 3.2): a green check when it is done
  // and a red cross when it needs fixing, with its words for screen readers and on hover. An optional setting nobody set up
  // shows no mark at all.
  let { mark }: { mark: SendingDomainMark } = $props();
  const view = $derived(statuses[mark.status]);
</script>

<span
  data-setting-mark={mark.status}
  title={mark.label}
  class={['inline-flex shrink-0', view.tone === 'clear' ? 'text-status-clear-mark' : 'text-status-verify-mark']}
>
  <view.icon aria-hidden="true" class="size-4" /><span class="sr-only">{mark.label}</span>
</span>
