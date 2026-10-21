<script lang="ts" module>
  import type { ConnectionStatus } from '@screenhire/realtime';

  // Shape, colour and label together (design-system section 2.2): circle for live, diamond while (re)connecting,
  // square when offline.
  export const liveStatus: Record<ConnectionStatus, { label: string; mark: string; text: string }> = {
    connected: { label: 'Live', mark: 'rounded-full bg-status-clear-mark', text: 'text-status-clear' },
    connecting: { label: 'Connecting…', mark: 'rotate-45 bg-status-review-mark', text: 'text-status-review' },
    reconnecting: { label: 'Reconnecting…', mark: 'rotate-45 bg-status-review-mark', text: 'text-status-review' },
    disconnected: { label: 'Offline', mark: 'bg-status-verify-mark', text: 'text-status-verify' },
  };
</script>

<script lang="ts">
  let { status }: { status: ConnectionStatus } = $props();

  const current = $derived(liveStatus[status]);
</script>

<!-- A status region, so a screen reader hears when live updates drop and come back. -->
<p role="status" class={['flex items-center gap-2 px-1.5 text-caption font-semibold', current.text]}>
  <span aria-hidden="true" class={['inline-block size-2 shrink-0', current.mark]}></span>
  <span><span class="sr-only">Live updates:</span> {current.label}</span>
</p>
