<script lang="ts">
  import UsersIcon from '~icons/lucide/users';
  import { createGetCandidate } from '@screenhire/api-client/svelte-query';
  import MetaItem from '#lib/components/meta-item.svelte';

  // "Also applied to" in an application's header: the link from the application to its candidate's profile
  // (/candidates/{id}), which lists every application the person made. It says how many other jobs they applied to when
  // it knows, counting only the jobs the reader sees (ADR-0015). It is a meta item until the header has its mini cards,
  // where it becomes the third. The link is there from the first paint and keeps its words; the count follows it as text.
  let { candidateId, applicationId }: { candidateId: string; applicationId: string } = $props();

  const candidate = createGetCandidate(() => candidateId);
  const others = $derived(candidate.data ? candidate.data.data.applications.filter((application) => application.id !== applicationId).length : undefined);
  const count = $derived(others === undefined ? '' : others === 0 ? 'no other jobs' : others === 1 ? '1 other job' : `${others} other jobs`);
</script>

<MetaItem label="Candidate profile" icon={UsersIcon}>
  <span><a href={`/candidates/${candidateId}`} class="inline-flex min-h-6 items-center underline underline-offset-2 pointer-coarse:min-h-11">Also applied to</a> {count}</span>
</MetaItem>
