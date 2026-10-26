<script lang="ts">
  import DetailLayout from '#lib/components/detail-layout.svelte';
  import DetailsRailSection from '#lib/components/details-rail-section.svelte';
  import DetailsRail from '#lib/components/details-rail.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
</script>

<DetailLayout>
  {#snippet rail()}
    <DetailsRail>
      <DetailsRailSection title="Details">
        <SummaryList><SummaryItem label="Source">Careers page</SummaryItem></SummaryList>
      </DetailsRailSection>
      <DetailsRailSection title="Tags"><p>Referral</p></DetailsRailSection>
    </DetailsRail>
  {/snippet}
  <section aria-label="Overview"><p>The tab</p></section>
</DetailLayout>
