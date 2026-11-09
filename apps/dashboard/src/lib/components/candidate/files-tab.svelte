<script lang="ts">
  import type { ApplicationDetail } from '@screenhire/api-client';
  import FileRow from '#lib/components/file-row.svelte';
  import Panel from '#lib/components/panel.svelte';

  // The candidate's Files tab (design system section 21): every file they gave, each opening in the full-window viewer from
  // its row; the page never embeds one (section 9). The tab's count is in its label, so the panel's title does not repeat it.
  let { detail }: { detail: ApplicationDetail } = $props();
</script>

<Panel title="Files">
  {#each detail.files as file (file.id)}
    <FileRow applicationId={detail.id} {file} description={`${detail.candidate.fullName}, ${detail.jobTitle}`} />
  {:else}
    <p class="text-muted-foreground">No files.</p>
  {/each}
</Panel>
