<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import Trash2Icon from '~icons/lucide/trash-2';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { automationLine } from '#lib/automation-sentence.js';
  import { isTerminal, placeOf, emailTemplateNames } from '#lib/workflow.js';
  import { cn } from '#lib/utils.js';

  // The automations as rows, grouped by the stage they belong to (design system section 11, Automation list): the
  // Automations tab's List view, which is the whole tab on a phone, and the lane sheet's contents. A row is the
  // automation's name and its sentence; where the page can edit, the whole row is the button that opens the automation as
  // a sentence, with its menu beside it, never inside it, for removing it (A18). An automation that is off says so after its name. `lane` narrows the list to one stage's (null is Any
  // stage's).
  let {
    workflow,
    lane,
    onopen,
    onremove,
    class: className,
  }: {
    workflow: HiringWorkflow;
    lane?: string | null;
    /** Opens the automation at this index. Without it the rows are plain text (a version that cannot be edited). */
    onopen?: (index: number) => void;
    /** Removes the automation with this id. */
    onremove?: (id: string) => void;
    class?: string;
  } = $props();

  // The removal waits for the row's menu to close: the focus is then on its button, which leaves with the row, so the Undo
  // notice takes it.
  let removing: string | null = null;
  function removeOnceClosed() {
    if (removing) onremove?.(removing);
    removing = null;
  }

  const emailTemplates = createListEmailTemplates();
  const team = createListTeamMembers();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const members = $derived(team.data?.data);

  // The open stages in order, then the stages that end a candidacy, then Any stage, as the map puts them (`placeOf`): an
  // arriving application and the checks under Applied, and an automation whose stage is gone under Any stage.
  const groups = $derived.by(() => {
    const keyOf = (index: number) => {
      const place = placeOf(workflow, workflow.automations[index]!);
      return place === 'any' ? null : place;
    };
    const all = [
      ...[...workflow.stages.filter((stage) => !isTerminal(stage.kind)), ...workflow.stages.filter((stage) => isTerminal(stage.kind))].map((stage) => ({ key: stage.id as string | null, name: stage.name })),
      { key: null as string | null, name: 'Any stage' },
    ];
    return all
      .filter((entry) => lane === undefined || entry.key === lane)
      .map((entry) => ({ ...entry, indexes: workflow.automations.flatMap((_, index) => (keyOf(index) === entry.key ? [index] : [])) }))
      .filter((entry) => entry.indexes.length > 0);
  });
</script>

{#if groups.length === 0}
  <p class="text-muted-foreground">No automations here yet.</p>
{:else}
  <div class={cn('flex flex-col gap-5', className)}>
    {#each groups as group (group.key ?? 'any')}
      <section class="flex flex-col gap-1" aria-label={group.name}>
        <h3 class="text-caption text-muted-foreground">{group.name}</h3>
        <ul class="flex flex-col divide-y divide-border-subtle rounded-lg border border-border-subtle">
          {#each group.indexes as index (workflow.automations[index]!.id)}
            {@const automation = workflow.automations[index]!}
            {#snippet row()}
              <span class="flex min-w-0 flex-col gap-0.5">
                <span class="font-semibold break-words">{automation.name || 'Untitled automation'}{#if automation.enabled === false}<span class="font-normal text-muted-foreground">, off</span>{/if}</span>
                <span class="text-muted-foreground break-words">{automationLine(automation, workflow, members, templateNames)}</span>
              </span>
            {/snippet}
            <li class="flex items-center">
              {#if onopen}
                <button
                  type="button"
                  class="flex min-w-0 flex-1 items-start px-3 py-2.5 text-left hover:bg-accent focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring pointer-coarse:min-h-11"
                  onclick={() => onopen(index)}
                >
                  {@render row()}
                </button>
                {#if onremove}
                  <MoreMenu label={`More for ${automation.name || 'Untitled automation'}`} size="icon" quiet onclosed={removeOnceClosed} class="mr-1.5 shrink-0">
                    <DropdownMenu.Item onSelect={() => (removing = automation.id)}><Trash2Icon aria-hidden="true" />Remove automation</DropdownMenu.Item>
                  </MoreMenu>
                {/if}
              {:else}
                <div class="flex w-full items-start px-3 py-2.5">{@render row()}</div>
              {/if}
            </li>
          {/each}
        </ul>
      </section>
    {/each}
  </div>
{/if}
