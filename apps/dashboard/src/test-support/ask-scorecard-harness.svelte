<script lang="ts">
  import type { ScorecardTemplateSummary, TeamMember, WorkflowStage } from '@screenhire/api-client';
  import AskScorecardSheet from '#lib/components/ask-scorecard-sheet.svelte';

  // The ask-for-a-scorecard sheet open on an application whose job's workflow has not loaded yet: the stage arrives
  // when the button is pressed, as the job's query answers after the page and the sheet are up.
  let { teammates, library, arriving }: { teammates: TeamMember[]; library: ScorecardTemplateSummary[]; arriving: WorkflowStage } = $props();
  let currentStage = $state<WorkflowStage | undefined>(undefined);
</script>

<button type="button" onclick={() => (currentStage = arriving)}>Workflow arrives</button>
<AskScorecardSheet open applicationId="a-1" {teammates} {library} {currentStage} ondone={() => {}} />
