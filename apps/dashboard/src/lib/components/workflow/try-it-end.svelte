<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { candidateName } from '#lib/try-it.js';

  // Try it's last step (design system section 11, Try it): the process in one line, then the decision it leads to. Looks
  // good publishes a template's draft (or saves a job's pipeline) through the page's own action; Change something is a menu
  // of the stages, and choosing one opens that stage's sheet. Neither shows where the process cannot be changed.
  let {
    stages,
    visited,
    emails,
    finish,
    heading = $bindable(),
    onchange,
    onrestart,
  }: {
    /** The pipeline's stages, for Change something. */
    stages: readonly WorkflowStage[];
    /** How many stages the candidate went through. */
    visited: number;
    /** How many different emails they got. */
    emails: number;
    /** The page's own action, such as "Looks good, publish"; undefined where there is nothing to publish or save. */
    finish?: { label: string; run: () => void; disabled?: boolean };
    heading?: HTMLElement;
    /** Opens a stage to change; undefined where the process cannot be changed. */
    onchange?: (stageId: string) => void;
    onrestart: () => void;
  } = $props();

  // The stage chosen in the menu opens once the menu has closed and given the focus back, so the sheet can return it there.
  let chosen = $state<string | null>(null);

  const count = (value: number, one: string, other: string) => `${value} ${value === 1 ? one : other}`;
  const summary = $derived(
    `${candidateName} went through ${count(visited, 'stage', 'stages')} and got ${emails === 0 ? 'no emails' : count(emails, 'email', 'emails')}.`,
  );
</script>

<div class="flex flex-col gap-4" data-try-it-step="end">
  <h3 bind:this={heading} tabindex="-1" class="text-heading focus-visible:outline-none">That's your hiring process</h3>
  <p>{summary}</p>
  <div class="flex flex-wrap items-center gap-3">
    {#if finish}
      <Button size="lg" onclick={finish.run} disabled={finish.disabled}>{finish.label}</Button>
    {/if}
    {#if onchange}
      <DropdownMenu.Root>
        <DropdownMenu.Trigger class={buttonVariants({ variant: 'outline', size: 'lg' })}>
          Change something<ChevronDownIcon aria-hidden="true" />
        </DropdownMenu.Trigger>
        <DropdownMenu.Content
          align="start"
          class="min-w-52"
          onCloseAutoFocus={() => {
            const stage = chosen;
            chosen = null;
            if (stage) setTimeout(() => onchange(stage));
          }}
        >
          <DropdownMenu.Label>Which stage?</DropdownMenu.Label>
          {#each stages as stage (stage.id)}
            <DropdownMenu.Item onSelect={() => (chosen = stage.id)}>{stage.name}</DropdownMenu.Item>
          {/each}
        </DropdownMenu.Content>
      </DropdownMenu.Root>
    {/if}
    <button
      type="button"
      class="rounded-sm underline underline-offset-2 hover:text-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring pointer-coarse:min-h-11"
      onclick={onrestart}>Start again</button
    >
  </div>
</div>
