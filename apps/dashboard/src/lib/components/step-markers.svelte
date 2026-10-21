<script lang="ts" module>
  export type StepState = 'done' | 'current' | 'upcoming';

  export interface Step {
    label: string;
    state: StepState;
  }
</script>

<script lang="ts">
  import CheckIcon from '@lucide/svelte/icons/check';

  // Steps in a sequence: square markers joined by a line (design system section 11). Never a count's wide pill (T4).
  // In a narrow container only the current step keeps its visible label; screen readers hear every label.
  let { steps, label }: { steps: readonly Step[]; label: string } = $props();
</script>

<ol aria-label={label} class="@container flex min-w-0 items-center">
  {#each steps as step, index (index)}
    {@const last = index === steps.length - 1}
    <li aria-current={step.state === 'current' ? 'step' : undefined} class={['flex items-center', step.state !== 'current' && 'min-w-0', !last && 'flex-1']}>
      <span class={['flex min-w-0 items-center gap-2', step.state === 'current' && 'shrink-0']}>
        <span
          aria-hidden="true"
          data-state={step.state}
          class={[
            'grid size-7 shrink-0 place-items-center rounded-step border bg-card text-caption tabular-nums',
            step.state === 'current' ? 'border-primary font-semibold text-primary' : 'border-input',
            step.state === 'done' ? 'text-foreground' : step.state === 'upcoming' && 'text-muted-foreground',
          ]}
        >
          {#if step.state === 'done'}<CheckIcon />{:else}{index + 1}{/if}
        </span>
        <span class={['truncate', step.state === 'current' ? 'font-semibold' : 'text-muted-foreground sr-only @xl:not-sr-only']}>
          {step.label}{#if step.state === 'done'}<span class="sr-only">, done</span>{/if}
        </span>
      </span>
      {#if !last}
        <span aria-hidden="true" class="mx-2 h-px min-w-3 flex-1 bg-border @xl:mx-3"></span>
      {/if}
    </li>
  {/each}
</ol>
