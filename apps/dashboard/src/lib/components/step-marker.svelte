<script lang="ts" module>
  export type StepState = 'done' | 'current' | 'upcoming';
</script>

<script lang="ts">
  import type { Snippet } from 'svelte';

  // One step marker (design system section 11), holding the step's number or a check. With no state it is a step in a
  // plain sequence, such as a workflow's stages.
  let { state, children }: { state?: StepState; children: Snippet } = $props();
</script>

<span
  aria-hidden="true"
  data-state={state}
  class={[
    'grid size-7 shrink-0 place-items-center rounded-step border bg-card text-caption tabular-nums',
    state === 'current' ? 'border-primary font-semibold text-primary' : 'border-input',
    state === 'upcoming' ? 'text-muted-foreground' : state !== 'current' && 'text-foreground',
  ]}
>
  {@render children()}
</span>
