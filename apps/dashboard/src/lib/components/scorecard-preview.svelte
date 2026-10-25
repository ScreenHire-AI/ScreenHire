<script lang="ts">
  import type { Recommendation, ScorecardQuestion } from '@screenhire/api-client';
  import ScorecardFields from '#lib/components/scorecard-fields.svelte';

  // A library scorecard as a reviewer fills it in (design system A8): the scorecard sheet's own fields, at the sheet's
  // width, for a sample candidate. The answers stay on this screen; nothing is submitted.
  let { questions }: { questions: readonly ScorecardQuestion[] } = $props();

  const uid = $props.id();
  let values = $state<Record<string, string>>({});
  let picks = $state<Record<string, string[]>>({});
  let recommendation = $state<Recommendation | ''>('');
  let note = $state('');
</script>

<section aria-labelledby={`${uid}-title`} class="@container flex w-full max-w-xl flex-col gap-5 rounded-panel border border-border bg-card p-4 shadow-surface">
  <h2 id={`${uid}-title`} class="text-heading">Scorecard for Jordan Lee</h2>
  <ScorecardFields {questions} bind:values bind:picks bind:recommendation bind:note idPrefix={uid} />
</section>
