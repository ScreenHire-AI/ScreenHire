<script lang="ts">
  import LibraryIcon from '~icons/lucide/library';
  import PlusIcon from '~icons/lucide/plus';
  import type { FormTemplateVersionDetail, HiringWorkflow } from '@screenhire/api-client';
  import { createListFormTemplates, getGetFormTemplateVersionQueryOptions } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { questionClashes, questionCount, type LibraryVersion } from '#lib/forms-timeline.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';
  import { followUpsOf } from '#lib/workflow.js';

  // "New form" on the Forms tab (workflow-engine.md section 7, Forms): the library's published forms this workflow does not
  // use yet, each with how many questions it asks. A form whose question ids another form here already uses cannot be
  // added, and says why, because a question id names one question across a hiring process. "Build a new form" makes one in
  // the library, which the template takes when it is published: in the footer's menu (A19), or, with nothing left to add, the
  // empty state's one action (E1).
  let {
    open = $bindable(false),
    workflow,
    onadd,
    onbuild,
  }: {
    open?: boolean;
    workflow: HiringWorkflow;
    onadd: (version: LibraryVersion) => void;
    /** Starts a new library form; left out where there is nowhere to come back to. */
    onbuild?: () => void;
  } = $props();

  const queryClient = useQueryClient();
  const library = createListFormTemplates();
  const used = $derived(new Set(followUpsOf(workflow).map((form) => form.formTemplateId).filter(Boolean)));
  const offered = $derived((library.data?.data.forms ?? []).filter((form) => !form.archived && form.latestPublishedVersion != null && !used.has(form.id)));

  // Each offered form's published content, read when the sheet opens, so a clash says so before anyone presses Add.
  let versions = $state<Record<string, FormTemplateVersionDetail>>({});
  let failure = $state('');
  $effect(() => {
    if (!open) return;
    failure = '';
    for (const form of offered) {
      if (versions[form.id]?.version === form.latestPublishedVersion) continue;
      queryClient
        .fetchQuery(getGetFormTemplateVersionQueryOptions(form.id, form.latestPublishedVersion!))
        .then((response) => (versions = { ...versions, [form.id]: response.data }))
        .catch((error) => (failure = describeError(error, 'Could not load the library')));
    }
  });

  const empty = $derived(!library.isPending && offered.length === 0);

  function build() {
    open = false;
    onbuild?.();
  }

  function add(formId: string) {
    const version = versions[formId];
    if (!version) return;
    onadd({ formTemplateId: version.formTemplateId, version: version.version, kind: version.kind, content: version.content });
    open = false;
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Add a form</Sheet.Title>
      <Sheet.Description>From your library. It is sent only by hand until you choose when it goes.</Sheet.Description>
    </Sheet.Header>
    <div class="flex flex-col gap-4 px-4 pb-4">
      {#if library.isPending}
        <div aria-hidden="true" class="flex flex-col gap-2">
          {#each [0, 1, 2] as row (row)}<Skeleton class="h-14 w-full" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the library…</p>
      {:else if empty}
        <p>Every published form in your library is here already.</p>
        <div>
          {#if onbuild}
            <Button variant="outline" size="lg" onclick={build}>Build a new form</Button>
          {:else}
            <a href="/templates?tab=forms" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Open the form library</a>
          {/if}
        </div>
      {:else}
        <ul class="flex flex-col divide-y divide-border rounded-lg border border-border">
          {#each offered as form (form.id)}
            {@const version = versions[form.id]}
            {@const clashes = version ? questionClashes(workflow, version.content) : []}
            <li class="flex items-center justify-between gap-3 px-3 py-2">
              <div class="flex min-w-0 flex-col">
                <span class="font-semibold">{form.name}</span>
                <span class="text-caption text-muted-foreground">
                  {questionCount(form.questions)}{form.kind === 'EqualityMonitoring' ? ', counted only' : ''}
                </span>
                {#if clashes.length > 0}
                  <span class="text-caption text-muted-foreground">
                    Another form here already asks {clashes.map((label) => `"${label}"`).join(', ')}. Change one of them in the library first.
                  </span>
                {/if}
              </div>
              <Button variant="outline" disabled={!version || clashes.length > 0} onclick={() => add(form.id)} aria-label={`Add ${form.name}`}>
                <PlusIcon aria-hidden="true" />Add
              </Button>
            </li>
          {/each}
        </ul>
      {/if}
      <FailureText message={failure} />
    </div>
    {#if !empty}
      <!-- Each row's Add is the action, so there is no primary one here, only the menu (A19). -->
      <Sheet.Footer>
        <MoreMenu label="More ways to add a form" class="mr-auto">
          {#if onbuild}
            <DropdownMenu.Item onSelect={build}><PlusIcon aria-hidden="true" />Build a new form</DropdownMenu.Item>
          {:else}
            <DropdownMenu.Item>
              {#snippet child({ props })}<a {...props} href="/templates?tab=forms"><LibraryIcon aria-hidden="true" />Open the form library</a>{/snippet}
            </DropdownMenu.Item>
          {/if}
        </MoreMenu>
      </Sheet.Footer>
    {/if}
  </Sheet.Content>
</Sheet.Root>
