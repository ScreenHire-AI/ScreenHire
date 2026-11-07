<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import XIcon from '~icons/lucide/x';
  import IconButton from '#lib/components/icon-button.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import type { SetupStep } from '#lib/setup-checklist.js';

  // The Set up your organization panel (design system section 11): the steps with a square marker each, a check once the
  // organization's data says it is done, and a link to the page where it is done. `SetupChecklist` gives it the steps.
  let { steps, ondismiss }: { steps: readonly SetupStep[]; ondismiss: () => void } = $props();
</script>

<Panel title="Set up your organization">
  {#snippet actions()}
    <IconButton label="Dismiss" onclick={ondismiss}><XIcon aria-hidden="true" /></IconButton>
  {/snippet}
  <ol aria-label="Setup steps" class="flex flex-col gap-3">
    {#each steps as step, index (step.id)}
      <li class="flex min-w-0 items-center gap-3">
        <StepMarker state={step.done ? 'done' : 'upcoming'}>
          {#if step.done}<CheckIcon />{:else}{index + 1}{/if}
        </StepMarker>
        <a href={step.href} class={['block min-w-0 truncate py-0.5 underline underline-offset-2 pointer-coarse:py-3', step.done ? 'text-muted-foreground' : 'font-semibold text-foreground']}>{step.label}</a>
        {#if step.done}<span class="sr-only">Done</span>{/if}
      </li>
    {/each}
  </ol>
</Panel>
