<script lang="ts">
  import type { ScorecardQuestion } from '@screenhire/api-client';
  import ScorecardBuilder from '#lib/components/scorecard-builder.svelte';

  // The form builder on a scorecard's questions: its own kinds, a Rating group and "What to look for" in place of help
  // text (ScorecardBuilder, which the scorecard template editor mounts). Nothing is saved.
  let questions = $state<ScorecardQuestion[]>([
    { id: 'problem_solving', type: 'Rating5', label: 'Problem solving', required: true, guidance: 'Breaks the exercise into steps and explains the trade-offs.', options: null },
    { id: 'dotnet', type: 'Rating10', label: '.NET and C#', required: true, guidance: 'Uses the language and the framework well.', options: null },
    {
      id: 'next_interview',
      type: 'MultiChoice',
      label: 'What should the next interview cover?',
      required: false,
      guidance: null,
      options: [
        { id: 'system_design', label: 'System design' },
        { id: 'testing', label: 'Testing' },
        { id: 'databases', label: 'Databases' },
      ],
    },
  ]);
</script>

<ScorecardBuilder bind:questions />
