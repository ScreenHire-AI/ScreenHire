<script lang="ts">
  import type { ApplicationSource } from '@screenhire/api-client';
  import IntegrityPanel from '#lib/components/integrity-panel.svelte';
  import ReviewLog from '#lib/components/review-log.svelte';

  // The integrity panel and the review log side by side, as the application page shows them, for component tests.
  let { applicationId, source }: { applicationId: string; source?: ApplicationSource } = $props();
</script>

<IntegrityPanel {applicationId} {source} />
<ReviewLog {applicationId} />
