<script lang="ts">
  import PlayIcon from '@lucide/svelte/icons/play';
  import { runManualAutomation, type Automation } from '@screenhire/api-client';
  import { createListAutomationRuns } from '@screenhire/api-client/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { describeError } from '#lib/query.js';

  // The job's automations a person runs by hand (a Manual trigger), each with Run (design system section 11). The
  // worker evaluates the run like any other, conditions and guardrails included, so Run asks for a run and does not
  // promise an outcome; what it did shows in Activity and Tasks when AutomationRunFinished arrives. Each click sends
  // its own Idempotency-Key, so a retried request runs nothing twice (workflow-engine.md §8). The page shows this panel
  // only to a role that can run automations, and only when the job has one.
  let { applicationId, automations }: { applicationId: string; automations: readonly Automation[] } = $props();

  const runs = createListAutomationRuns(() => applicationId);
  const lastRun = (automationId: string) => (runs.data?.data ?? []).findLast((run) => run.automationId === automationId && run.trigger === 'Manual');

  let running = $state<string | null>(null);
  let failure = $state('');
  let announcement = $state('');

  async function run(automation: Automation) {
    running = automation.id;
    failure = '';
    try {
      const response = await runManualAutomation(applicationId, automation.id, { headers: { 'Idempotency-Key': crypto.randomUUID() } });
      if (response.status === 202) {
        announcement = `Asked to run "${automation.name}". What it does shows in Activity and Tasks.`;
      } else {
        // The plain client returns refusals rather than throwing: a 404 is an automation gone from the job's workflow,
        // a 400 says why it cannot run by hand.
        const problems = response.status === 400 ? Object.values(response.data.errors ?? {}).flat() : [];
        failure =
          response.status === 404
            ? `"${automation.name}" is no longer in the job's workflow. Reload the page.`
            : problems.length > 0
              ? `Could not run "${automation.name}": ${problems.join(' ')}`
              : `Could not run "${automation.name}" (HTTP ${response.status}).`;
        announcement = failure;
      }
    } catch (error) {
      failure = describeError(error, `Could not run "${automation.name}"`);
      announcement = failure;
    } finally {
      running = null;
    }
  }
</script>

<Panel title="Automations">
  <div class="flex flex-col gap-3">
    <ul aria-label="Automations you can run" class="flex flex-col">
      {#each automations as automation (automation.id)}
        {@const last = lastRun(automation.id)}
        <li class="flex items-center gap-3 border-b border-border-subtle py-3 first:pt-0 last:border-b-0 last:pb-0">
          <div class="flex min-w-0 flex-1 flex-col gap-0.5">
            <p class="flex min-w-0 font-semibold"><TruncatedText text={automation.name} /></p>
            {#if last}
              <p class="text-caption text-muted-foreground">Last run <RelativeTime at={last.createdAt} /></p>
            {/if}
          </div>
          <Button variant="outline" class="shrink-0" disabled={running !== null} aria-label={`Run, ${automation.name}`} onclick={() => run(automation)}>
            <PlayIcon aria-hidden="true" />Run
          </Button>
        </li>
      {/each}
    </ul>
    <FailureText message={failure} />
    <p role="status" class={['text-muted-foreground empty:hidden', failure && 'sr-only']}>{failure ? '' : announcement}</p>
  </div>
</Panel>
