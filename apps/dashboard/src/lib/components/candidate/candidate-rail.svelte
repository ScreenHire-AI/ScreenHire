<script lang="ts">
  import type { ApplicationDetail } from '@screenhire/api-client';
  import ApplicationTags from '#lib/components/application-tags.svelte';
  import DetailsRail from '#lib/components/details-rail.svelte';
  import DetailsRailSection from '#lib/components/details-rail-section.svelte';
  import FileRow from '#lib/components/file-row.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { sourceName } from '#lib/format.js';

  // A candidate's details rail (design system sections 7 and 21): the job and where they are in it, contact, files and
  // tags, the same on every tab. Stage leads, because the header holds tabs, not a facts row. A file opens in the
  // full-window viewer from its row; the page never embeds one (section 9).
  let { detail, editable }: { detail: ApplicationDetail; editable: boolean } = $props();

  const linkClass = 'pointer-coarse:-my-3.5 pointer-coarse:py-3.5';
</script>

<DetailsRail>
  <DetailsRailSection title="Details">
    <SummaryList>
      <SummaryItem label="Stage">{detail.stage.name}</SummaryItem>
      <SummaryItem label="Job"><TruncatedText text={detail.jobTitle} href={`/jobs/${detail.jobId}`} underlined class={linkClass} /></SummaryItem>
      <SummaryItem label="Applied"><RelativeTime at={detail.submittedAt} /></SummaryItem>
      <SummaryItem label="Source">{sourceName(detail.source)}</SummaryItem>
      <SummaryItem label="Email"><TruncatedText text={detail.candidate.email} href={`mailto:${detail.candidate.email}`} underlined class={linkClass} /></SummaryItem>
      {#if detail.candidate.phone}
        <SummaryItem label="Phone"><TruncatedText text={detail.candidate.phone} href={`tel:${detail.candidate.phone}`} underlined class={linkClass} /></SummaryItem>
      {/if}
    </SummaryList>
  </DetailsRailSection>
  <DetailsRailSection title="Files">
    {#each detail.files as file (file.id)}
      <FileRow applicationId={detail.id} {file} description={`${detail.candidate.fullName}, ${detail.jobTitle}`} />
    {:else}
      <p class="text-muted-foreground">No files.</p>
    {/each}
  </DetailsRailSection>
  <ApplicationTags applicationId={detail.id} tags={detail.tags} {editable} />
</DetailsRail>
