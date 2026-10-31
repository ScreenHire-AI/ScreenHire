<script lang="ts">
  import type { ApplicationSource } from '@screenhire/api-client';
  import VerificationPanel from '#lib/components/verification-panel.svelte';
  import ReviewLog from '#lib/components/review-log.svelte';

  // The verification panel and the review log side by side, as the application page shows them, for component tests.
  let { applicationId, source }: { applicationId: string; source?: ApplicationSource } = $props();
</script>

<VerificationPanel {applicationId} {source} />
<ReviewLog {applicationId} />
