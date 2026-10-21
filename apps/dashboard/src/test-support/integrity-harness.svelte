<script lang="ts">
  import IntegrityPanel from '#lib/components/integrity-panel.svelte';
  import ReviewLog from '#lib/components/review-log.svelte';

  // The integrity panel and the review log side by side, as the application page shows them, for component tests.
  let { applicationId }: { applicationId: string } = $props();
</script>

<IntegrityPanel {applicationId} />
<ReviewLog {applicationId} />
