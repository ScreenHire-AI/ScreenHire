<script lang="ts">
  import type { Automation, DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates, createListRoles, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import { tick } from 'svelte';
  import XIcon from '~icons/lucide/x';
  import IconButton from '#lib/components/icon-button.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { sentenceOf, stepRemoval, textFieldFor, type Blank, type Clause, type Part, type SentenceContext } from '#lib/automation-sentence.js';
  import { sendableTemplates } from '#lib/email.js';
  import { membersWith } from '#lib/team.js';
  import { builtInEmailTemplates, errorsAt, errorsUnder, isEmptyTextStep } from '#lib/workflow.js';
  import AutomationBlank from './automation-blank.svelte';
  import { keywordClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import InterviewFields from './interview-fields.svelte';

  // One automation as When, If and Then, one clause a line under its keyword (design system section 11, Automation
  // sentence), the sheet's Sentence tab: "When [someone applies]", "If [their answer to Right to work] [is] [No]", "Then [ask]
  // [the team] to review", "and [add the tag] [...]". Every blank is a short list of plain choices from
  // `#lib/automation-sentence.ts`; a joining word starts a line, a plus ends a section, and the x at a clause's end takes it
  // away, with Undo (A18). The validator's messages sit under the sections, and the part's blank is marked. The Rules tab is
  // the full form over the same automation: nothing here is stored but the automation.
  let {
    automation = $bindable(),
    index,
    workflow,
    errors,
    onundo,
  }: {
    automation: Automation;
    index: number;
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** Offers to take back what was just removed: the sentence to show, and the automation with it again. */
    onundo?: (message: string, restore: (current: Automation) => Automation) => void;
  } = $props();

  const uid = $props.id();
  const path = $derived(`automations[${index}]`);
  const emailTemplates = createListEmailTemplates();
  const team = createListTeamMembers();
  const roles = createListRoles();
  const context: SentenceContext = $derived({
    emailTemplates: emailTemplates.data ? sendableTemplates(emailTemplates.data.data.templates) : builtInEmailTemplates,
    // Roles without Complete tasks could not complete the review task, so they are not offered.
    reviewers: membersWith(team.data?.data ?? [], roles.data?.data.roles ?? [], 'FillInScorecards').map((member) => ({ userId: member.userId, displayName: member.displayName })),
    teamKnown: team.isSuccess,
  });
  const sections = $derived(sentenceOf(workflow, index, context));
  const interviewSteps = $derived(automation.actions.flatMap((action, step) => (action.type === 'ScheduleInterview' ? [step] : [])));
  const messages = $derived([...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsUnder(errors, path).filter((error) => !error.path.startsWith(`${path}.name`) && !interviewField(error.path)).map((error) => error.message)]);

  // The four interview fields show their messages under themselves (F6).
  const interviewField = (errorPath: string) => /\.actions\[\d+\]\.(interviewerUserIds|interviewMinutes|interviewWindowDays|interviewTimeZone)$/.test(errorPath);

  let root = $state<HTMLElement>();
  // The list that is open, by blank. A step added with an empty text (a new tag) opens its field at once: the text is the
  // first thing it asks for.
  let openId = $state<string | null>(null);

  function apply(next: Automation) {
    const before = automation.actions.length;
    automation = next;
    if (next.actions.length > before) {
      const added = next.actions.at(-1)!;
      const field = textFieldFor(added.type);
      if (field && !(added[field] ?? '').trim()) openId = `action-${next.actions.length - 1}-arg`;
    }
  }

  // A step that asks for text and is left empty is dropped, with Undo, so a blank step is never a problem to fix. The sheet
  // can close on an open field and take the automation with it (`tidyAutomation`), so there may be none by now.
  function closed(blank: Blank) {
    if (openId === blank.id) openId = null;
    const match = /^action-(\d+)-arg$/.exec(blank.id);
    if (!match) return;
    const step = Number(match[1]);
    const action = (automation as Automation | undefined)?.actions[step];
    if (!action || !isEmptyTextStep(action)) return;
    const removal = stepRemoval(automation, step, 'Removed the empty step.');
    automation = removal.apply();
    onundo?.(removal.message, removal.restore);
    // Once the list that closed has let go of the focus: its chip is gone, so the plus that added the step takes it.
    setTimeout(() => {
      if (document.activeElement?.closest('[data-slot="popover-content"], [data-clause]')) return;
      root?.querySelector<HTMLElement>('[data-section="Then"] [data-adds]')?.focus();
    });
  }

  // Taking a clause away moves the focus to the x that takes its place, else to the section's plus, so a keyboard user stays
  // where they were working.
  async function remove(clause: Clause, section: string, at: number) {
    if (!clause.remove) return;
    const { message, restore } = clause.remove;
    automation = clause.remove.apply();
    onundo?.(message, restore);
    await tick();
    const group = root?.querySelector(`[data-section="${section}"]`);
    const rest = group?.querySelectorAll<HTMLElement>('[data-remove-clause]');
    (rest?.[Math.min(at, (rest?.length ?? 0) - 1)] ?? group?.querySelector<HTMLElement>('[data-adds]'))?.focus();
  }

  const invalid = (blank: Blank) => errorsUnder(errors, blank.errorPath).length > 0;
</script>

{#snippet blankChip(blank: Blank)}
  <AutomationBlank
    {blank}
    invalid={invalid(blank)}
    describedby={invalid(blank) ? `${uid}-errors` : undefined}
    bind:open={() => openId === blank.id, (next) => (openId = next ? blank.id : openId === blank.id ? null : openId)}
    onapply={apply}
    onclose={closed}
  />
{/snippet}

{#snippet partOf(part: Part)}
  {#if part.type === 'text'}<span>{part.text}</span>{:else}{@render blankChip(part.blank)}{/if}
{/snippet}

<article bind:this={root} aria-label={automation.name || 'Untitled automation'} class="flex flex-col gap-4">
  <div class="flex flex-col gap-4 rounded-lg bg-secondary p-3 text-reading" data-sentence>
    {#each sections as section (section.keyword)}
      <section aria-label={section.keyword} data-section={section.keyword} class="flex flex-col items-start gap-2">
        <Badge variant="muted" class={keywordClass}>{section.keyword}</Badge>
        {#if section.clauses.length > 0}
          <ul class="flex w-full flex-col gap-2">
            {#each section.clauses as clause, at (clause.id)}
              <li class="flex flex-wrap items-center gap-x-1.5 gap-y-1" data-clause>
                {#if clause.joiner}
                  {@render partOf(clause.joiner)}
                {/if}
                {#each clause.parts.slice(0, -1) as part, position (part.type === 'blank' ? part.blank.id : position)}{@render partOf(part)}{/each}
                <!-- The x wraps with what it follows, so a long clause never leaves it alone on a line. -->
                <span class="inline-flex max-w-full min-w-0 items-center gap-1.5">
                  {#if clause.parts.length > 0}{@render partOf(clause.parts.at(-1)!)}{/if}
                  {#if clause.remove}
                    <IconButton size="icon-sm" label={clause.remove.label} data-remove-clause onclick={() => remove(clause, section.keyword, at)}><XIcon aria-hidden="true" /></IconButton>
                  {/if}
                </span>
              </li>
            {/each}
          </ul>
        {/if}
        {#if section.add}{@render blankChip(section.add)}{/if}
      </section>
    {/each}
  </div>

  {#each interviewSteps as step (step)}
    <InterviewFields bind:action={automation.actions[step]!} actionPath={`${path}.actions[${step}]`} {errors} />
  {/each}

  <FieldErrors id={`${uid}-errors`} {messages} />
</article>
