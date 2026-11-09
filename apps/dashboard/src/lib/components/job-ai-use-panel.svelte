<script lang="ts">
  import type { AiUsePolicy, JobDetail } from '@screenhire/api-client';
  import { createSetJobAiUse } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { aiUseName } from '#lib/workflow.js';
  import { selectClass } from '#lib/components/workflow/controls.js';

  // A job's own AI-use policy, or none, which follows the organization's (Verification). ScreenHire asks the question on
  // every application either way, and no automation reads the answer. It saves on its own, apart from the job's details,
  // because it is not posting content: it does not mark the job changed or republish it. Its Save is an outline button, so
  // the page keeps its one filled button (A1).
  let { job }: { job: JobDetail } = $props();

  const policies: AiUsePolicy[] = ['Allowed', 'AllowedWithDisclosure', 'NotAllowed'];
  const queryClient = useQueryClient();
  const save = createSetJobAiUse(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));

  const saved = $derived(job.aiUse.override ?? '');
  let chosen = $state<string | null>(null);
  const value = $derived(chosen ?? saved);
  let failure = $state('');
  let notice = $state('');

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    failure = '';
    notice = '';
    try {
      await save.mutateAsync({ jobId: job.id, data: { policy: value === '' ? null : (value as AiUsePolicy) } });
      chosen = null;
      notice = 'Saved.';
    } catch (error) {
      failure = describeError(error, 'Could not save the AI use policy');
    }
  }
</script>

<form id="ai-use" onsubmit={submit} class="scroll-mt-20">
  <Panel title="AI use while applying">
    <div class="flex max-w-form flex-col gap-3">
      <select class={selectClass} aria-label="AI use while applying" aria-describedby="job-ai-use-note" {value} onchange={(event) => ((chosen = event.currentTarget.value), (notice = ''))}>
        <option value="">Follow the organization ({aiUseName(job.aiUse.organizationDefault)})</option>
        {#each policies as policy (policy)}<option value={policy}>{aiUseName(policy)}</option>{/each}
      </select>
      <p id="job-ai-use-note" class="text-muted-foreground">Asked on every application. No automation uses the answer.</p>
      <FailureText message={failure} />
      <div class="flex items-center gap-3">
        <Button type="submit" variant="outline" disabled={value === saved || save.isPending}>Save AI use</Button>
        {#if notice}<span role="status" class="text-muted-foreground">{notice}</span>{/if}
      </div>
    </div>
  </Panel>
</form>
