<script lang="ts">
  import type { Automation, DefinitionError, HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates, createListRoles, createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import { sentenceOf, textFieldFor, type Blank, type SentenceContext } from '#lib/automation-sentence.js';
  import { sendableTemplates } from '#lib/email.js';
  import { membersWith } from '#lib/team.js';
  import { getUndo } from '#lib/undo-context.js';
  import { builtInEmailTemplates, errorsAt, errorsUnder } from '#lib/workflow.js';
  import AutomationBlank from './automation-blank.svelte';
  import FieldErrors from './field-errors.svelte';
  import InterviewFields from './interview-fields.svelte';

  // One automation as a sentence with blanks (design system section 11, Automation sentence), the sheet's Sentence tab under
  // the automation's Name and On (`AutomationName`): "When [someone applies] and [their answer to Right to work is No], [ask
  // the team to review] and [add the tag ...]". Every blank is a short list of plain choices from `#lib/automation-sentence.ts`;
  // "and if" and "then also" add parts. The validator's messages for each part sit under the sentence, and the part's blank is
  // marked. The Rules tab is the full form over the same automation: nothing here is stored but the automation.
  let {
    automation = $bindable(),
    index,
    workflow,
    errors,
  }: {
    automation: Automation;
    index: number;
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
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
  const parts = $derived(sentenceOf(workflow, index, context));
  const interviewSteps = $derived(automation.actions.flatMap((action, step) => (action.type === 'ScheduleInterview' ? [step] : [])));
  const messages = $derived([...errorsAt(errors, path), ...errorsAt(errors, `${path}.id`), ...errorsUnder(errors, path).filter((error) => !error.path.startsWith(`${path}.name`) && !interviewField(error.path)).map((error) => error.message)]);

  // The four interview fields show their messages under themselves (F6).
  const interviewField = (errorPath: string) => /\.actions\[\d+\]\.(interviewerUserIds|interviewMinutes|interviewWindowDays|interviewTimeZone)$/.test(errorPath);

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

  // A step that asks for text and is left empty is dropped, with Undo, so a blank step is never a problem to fix.
  const undo = getUndo();
  function closed(blank: Blank) {
    if (openId === blank.id) openId = null;
    const match = /^action-(\d+)-arg$/.exec(blank.id);
    if (!match) return;
    const step = Number(match[1]);
    const action = automation.actions[step];
    const field = action ? textFieldFor(action.type) : null;
    if (!action || !field || (action[field] ?? '').trim() !== '') return;
    automation = { ...automation, actions: automation.actions.filter((_, at) => at !== step) };
    undo?.offer('Removed the empty step.', async () => {
      automation = { ...automation, actions: [...automation.actions.slice(0, step), action, ...automation.actions.slice(step)] };
      return null;
    });
  }

  const invalid = (blank: Blank) => errorsUnder(errors, blank.errorPath).length > 0;
  const partKey = (part: (typeof parts)[number], at: number) => (part.type === 'blank' ? part.blank.id : `text-${at}`);
</script>

<article aria-label={automation.name || 'Untitled automation'} class="flex flex-col gap-4">
  <p class="rounded-lg bg-secondary p-3 text-reading leading-10 pointer-coarse:leading-14 break-words" data-sentence>
    {#each parts as part, at (partKey(part, at))}{#if at > 0 && !(part.type === 'text' && part.tight)}{' '}{/if}{#if part.type === 'text'}<span>{part.text}</span>{:else}<AutomationBlank
          blank={part.blank}
          invalid={invalid(part.blank)}
          describedby={invalid(part.blank) ? `${uid}-errors` : undefined}
          bind:open={() => openId === part.blank.id, (next) => (openId = next ? part.blank.id : openId === part.blank.id ? null : openId)}
          onapply={apply}
          onclose={closed}
        />{/if}{/each}
  </p>

  {#each interviewSteps as step (step)}
    <InterviewFields bind:action={automation.actions[step]!} actionPath={`${path}.actions[${step}]`} {errors} />
  {/each}

  <FieldErrors id={`${uid}-errors`} {messages} />
</article>
