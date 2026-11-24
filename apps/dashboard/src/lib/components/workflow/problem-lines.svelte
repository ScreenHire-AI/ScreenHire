<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import ProblemIcon from '#lib/components/problem-icon.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { describePath, isBlankStep } from '#lib/workflow.js';

  // The validator's errors as compact lines in the Notice's style, above a workflow's tabs (design system section 11,
  // Notice): one line a problem, three shown and the rest behind "and N more". The `ValidationBadge` beside the title says
  // there are some, and its Fix button brings the focus here. A message names its own thing, so it is shown as it is, with no
  // heading over it. Where the workflow can be edited, each line has a Fix button that opens the place it is in and focuses
  // the field (`onfix`), and Remove step where the problem is a step with nothing in it (`onremove`). Nothing renders while
  // there are none.
  let {
    label = 'Problems to fix',
    workflow,
    errors,
    onfix,
    onremove,
  }: {
    /** The region's name for a screen reader. */
    label?: string;
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    onfix?: (path: string) => void;
    /** Removes the step at this index of the automation at that one. */
    onremove?: (automation: number, step: number) => void;
  } = $props();

  const uid = $props.id();
  const few = 3;
  let all = $state(false);
  const shown = $derived(all ? errors : errors.slice(0, few));
  const rest = $derived(errors.length - few);

  /** The blank step a problem is about, if it is one. */
  function blankStep(path: string): { automation: number; step: number } | null {
    const match = /^automations\[(\d+)\]\.actions\[(\d+)\]/.exec(path);
    if (!match) return null;
    const action = workflow.automations[Number(match[1])]?.actions[Number(match[2])];
    return action && isBlankStep(action) ? { automation: Number(match[1]), step: Number(match[2]) } : null;
  }
</script>

{#if errors.length > 0}
  <section aria-label={label} data-slot="notice" class="flex flex-col rounded-panel bg-status-verify-soft px-3.5 py-2 text-status-verify">
    <ul id={`${uid}-list`} class="flex flex-col">
      {#each shown as error, at (at)}
        {@const blank = onremove ? blankStep(error.path) : null}
        <li class="flex flex-wrap items-center gap-x-3 gap-y-1 py-1">
          <span class="flex min-w-0 flex-[1_1_16rem] items-start gap-3"><ProblemIcon class="mt-0.5" /><span class="min-w-0 break-words">{error.message}</span></span>
          {#if onfix}
            <span class="flex shrink-0 gap-2">
              {#if blank}
                <Button variant="outline" size="sm" onclick={() => onremove?.(blank.automation, blank.step)}>Remove step<span class="sr-only">: {describePath(workflow, error.path)}</span></Button>
              {/if}
              <Button variant="outline" size="sm" data-problem-fix onclick={() => onfix(error.path)}>Fix<span class="sr-only">: {describePath(workflow, error.path)}</span></Button>
            </span>
          {/if}
        </li>
      {/each}
    </ul>
    {#if rest > 0}
      <div class="py-1 pl-7">
        <Button variant="outline" size="sm" aria-expanded={all} aria-controls={`${uid}-list`} onclick={() => (all = !all)}>
          {all ? 'Show fewer' : `and ${rest} more`}
          <ChevronDownIcon aria-hidden="true" class={['motion-safe:transition-transform', all && 'rotate-180']} />
        </Button>
      </div>
    {/if}
  </section>
{/if}
