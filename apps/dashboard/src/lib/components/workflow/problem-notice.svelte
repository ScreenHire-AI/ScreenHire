<script lang="ts">
  import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import ProblemIcon from '#lib/components/problem-icon.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { removeAt } from '#lib/workflow.js';
  import { labelClass } from './controls.js';

  // The Test tab's notice while a problem blocks it (design system section 11, Notice): what is wrong in the workflow's own
  // words, and the fix inline where it is a small one. Today that is an Add a tag step with no tag: type the tag, or remove
  // the step. Any other problem has a Fix button that opens the place it is in (`onfix`). The samples run again by
  // themselves once the workflow is valid.
  let {
    workflow = $bindable(),
    errors,
    onfix,
    editable = true,
  }: { workflow: HiringWorkflow; errors: readonly DefinitionError[]; onfix: (path: string) => void; editable?: boolean } = $props();

  const first = $derived(errors[0]);
  // The blank tag step the first problem is about: which automation and which step, if the path names one.
  const blankTag = $derived.by(() => {
    const match = first ? /^automations\[(\d+)\]\.actions\[(\d+)\]/.exec(first.path) : null;
    if (!match) return null;
    const automation = workflow.automations[Number(match[1])];
    const action = automation?.actions[Number(match[2])];
    return automation && action?.type === 'AddTag' && !action.tag?.trim() ? { automationIndex: Number(match[1]), actionIndex: Number(match[2]) } : null;
  });
  let tag = $state('');

  function addTag() {
    if (!blankTag || !tag.trim()) return;
    workflow.automations[blankTag.automationIndex]!.actions[blankTag.actionIndex]!.tag = tag.trim();
    tag = '';
  }

  // A step is a row of the draft: removing it needs no confirmation (section 10). An automation left with no step goes too.
  function removeStep() {
    if (!blankTag) return;
    const automation = workflow.automations[blankTag.automationIndex]!;
    if (automation.actions.length <= 1) workflow.automations = removeAt(workflow.automations, blankTag.automationIndex);
    else automation.actions = removeAt(automation.actions, blankTag.actionIndex);
    tag = '';
  }
</script>

<div role="status" data-slot="notice" class="flex flex-col gap-3 rounded-panel bg-status-verify-soft px-3.5 py-3 text-status-verify">
  <p class="flex items-center gap-3 font-semibold"><ProblemIcon />Paused until {errors.length === 1 ? '1 thing is' : `${errors.length} things are`} fixed</p>
  <p>{first?.message}{editable ? ' Testing and publishing resume after the fix.' : ' This version cannot be changed, so it cannot be tested.'}</p>
  {#if !editable}
    <!-- Read only: nothing to fix here. -->
  {:else if blankTag}
    <div class="flex flex-wrap items-end gap-2">
      <label class={[labelClass, 'w-full max-w-56']}>
        Tag name
        <Input class="bg-card" bind:value={tag} maxlength={50} onkeydown={(event) => event.key === 'Enter' && (event.preventDefault(), addTag())} />
      </label>
      <Button variant="outline" disabled={!tag.trim()} onclick={addTag}>Add the tag</Button>
      <Button variant="outline" onclick={removeStep}>Remove that step</Button>
    </div>
  {:else if first}
    <div><Button variant="outline" onclick={() => onfix(first.path)}>Fix it</Button></div>
  {/if}
</div>
