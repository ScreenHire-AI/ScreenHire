<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import { Button } from '#lib/components/ui/button/index.js';
  import { groupedRecipes, withRecipe, type Recipe } from '#lib/automation-recipes.js';

  // Add an automation (design system section 11, Recipe list): ready-made automations for this template's own questions
  // and stages, in groups, one line each. Adding one hides it, and says so once with a way to open it. Write your own opens
  // an empty sentence. `lane` narrows the list to the recipes that hang under one stage. No recipe turns anyone down on a
  // check, so there is nothing here to explain.
  let {
    workflow = $bindable(),
    lane,
    onwrite,
    onedit,
  }: {
    workflow: HiringWorkflow;
    lane?: string | null;
    /** Adds an empty automation in this lane and opens it. */
    onwrite: () => void;
    /** Opens an automation, by id. */
    onedit: (id: string) => void;
  } = $props();

  const uid = $props.id();
  const groups = $derived(groupedRecipes(workflow, lane));
  let added = $state<{ id: string; title: string } | null>(null);

  function add(recipe: Recipe) {
    workflow.automations = withRecipe(workflow, recipe).automations;
    added = { id: recipe.automation.id, title: recipe.title };
  }
</script>

<div class="flex flex-col gap-5">
  <div class="flex flex-wrap items-center justify-between gap-3">
    <Button variant="outline" size="lg" onclick={onwrite}>Write your own</Button>
    <div role="status" class="flex min-w-0 flex-wrap items-center gap-2 text-muted-foreground">
      {#if added}
        <span class="min-w-0 break-words">Added "{added.title}".</span>
        <Button variant="outline" size="sm" onclick={() => onedit(added!.id)}>Edit{' '}<span class="sr-only">{added.title}</span></Button>
      {/if}
    </div>
  </div>

  {#each groups as group, at (group.group)}
    <section class="flex flex-col gap-2" aria-labelledby={`${uid}-group-${at}`}>
      <h3 id={`${uid}-group-${at}`} class="text-caption text-muted-foreground">{group.group}</h3>
      <ul class="flex flex-col divide-y divide-border-subtle rounded-lg border border-border-subtle">
        {#each group.recipes as recipe (recipe.key)}
          <li class="flex items-center justify-between gap-3 px-3 py-2.5">
            <span class="flex min-w-0 flex-col gap-0.5">
              <span class="font-semibold break-words">{recipe.title}</span>
              <span class="text-muted-foreground break-words">{recipe.line}</span>
            </span>
            <Button variant="outline" onclick={() => add(recipe)}>Add{' '}<span class="sr-only">{recipe.title}</span></Button>
          </li>
        {/each}
      </ul>
    </section>
  {:else}
    <p class="text-muted-foreground">{lane ? 'No ready-made automations for this stage.' : 'Every ready-made automation for this template is added.'}</p>
  {/each}
</div>
