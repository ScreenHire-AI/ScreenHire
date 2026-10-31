<script lang="ts">
  import { createGetCandidate } from '@screenhire/api-client/svelte-query';
  import DateText from '#lib/components/date-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { stageLabel } from '#lib/talent-pool.js';

  // The candidate's other applications to the organization, each a link to its page (docs/architecture/talent-pool.md):
  // an application added from the talent pool links back to the one the candidate was kept in mind from, and that one
  // forward to it. Only the jobs the reader sees (ADR-0015). Hidden when there are none, which is most candidates.
  let { candidateId, applicationId }: { candidateId: string; applicationId: string } = $props();

  const candidate = createGetCandidate(() => candidateId);
  const others = $derived((candidate.data?.data.applications ?? []).filter((application) => application.id !== applicationId));
</script>

{#if others.length > 0}
  <Panel title="Other applications" count={others.length}>
    <ul class="flex flex-col divide-y divide-border-subtle">
      {#each others as application (application.id)}
        <li class="flex min-w-0 items-center gap-3 py-2 first:pt-0 last:pb-0">
          <span class="flex min-w-0 flex-1"><TruncatedText text={application.jobTitle} href={`/applications/${application.id}`} underlined /></span>
          <Badge variant="muted">{stageLabel(application)}</Badge>
          <DateText at={application.submittedAt} class="shrink-0 text-muted-foreground" />
        </li>
      {/each}
    </ul>
  </Panel>
{/if}
