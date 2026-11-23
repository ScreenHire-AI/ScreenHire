<script lang="ts">
  import DownloadIcon from '~icons/lucide/download';
  import { getGetApplicationDecisionRecordQueryOptions } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { downloadJson } from '#lib/csv.js';
  import { describeError } from '#lib/query.js';

  // The application's decision record as one file (docs/architecture/data-requests.md, Decision record): what the checks
  // found, what the team did and why, and how the application moved, for answering a candidate's access or review request.
  // Shown only where the deployment serves it (`decisionRecordEnabled`). It goes to the person who asked for it, never
  // to the candidate.
  let { applicationId }: { applicationId: string } = $props();

  const queryClient = useQueryClient();
  let busy = $state(false);
  let failure = $state('');

  async function download() {
    busy = true;
    failure = '';
    try {
      const record = (await queryClient.fetchQuery({ ...getGetApplicationDecisionRecordQueryOptions(applicationId), staleTime: 0 })).data;
      downloadJson(`decision-record-${applicationId}.json`, record);
    } catch (error) {
      failure = describeError(error, 'The decision record could not be downloaded');
    } finally {
      busy = false;
    }
  }
</script>

<div class="flex flex-col items-start gap-2">
  <Button variant="outline" disabled={busy} onclick={download}><DownloadIcon aria-hidden="true" />Download decision record</Button>
  <p class="text-caption text-muted-foreground">Everything on this application in one file, for a candidate's request to see or review a decision.</p>
  <FailureText message={failure} />
</div>
