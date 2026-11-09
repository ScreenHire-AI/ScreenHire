// Ready-made automations for a template's own questions and stages (design system section 11, Recipe list). Each recipe is
// one automation the Add automation list offers in a group, in one line. Nothing here names a person: a review is asked of
// "the team" (the sentence picks a teammate), and no recipe turns anyone down on a check (CLAUDE.md non-negotiable 2).
// A recipe is hidden once the workflow already does the same thing, whoever wrote it, so a starting point's own "thank them
// when they apply" is never offered twice.
import type { Automation, AutomationAction, AutomationCondition, HiringWorkflow } from '@screenhire/api-client';
import { breaksWorkflow } from './automation-sentence.js';
import { builtInEmailTemplates, isTerminal, laneOf } from './workflow.js';

export const recipeGroups = ['Keep candidates in the loop', 'Check the must-haves', 'Look closer when the checks suggest it'] as const;
export type RecipeGroup = (typeof recipeGroups)[number];

export interface Recipe {
  /** Stable across edits of the workflow: the automation's id is `recipe-{key}`. */
  key: string;
  group: RecipeGroup;
  title: string;
  /** What it does, in one sentence. */
  line: string;
  /** The stage whose lane it hangs under, or null for the whole workflow. */
  lane: string | null;
  automation: Automation;
}

/** The validator's id limit (DefinitionValidator.Identifier): 1 to 40 characters. */
const maxIdLength = 40;

/**
 * The automation id for a recipe: `recipe-{key}`, or, when a long stage or question id would take it past 40 characters,
 * its start and a short hash of the whole key, so the id stays stable and two long keys with one start still differ.
 */
export function recipeId(key: string): string {
  const id = `recipe-${key}`;
  if (id.length <= maxIdLength) return id;
  // FNV-1a, 32 bits: enough to tell a template's few long keys apart.
  let hash = 0x811c9dc5;
  for (let at = 0; at < key.length; at++) hash = Math.imul(hash ^ key.charCodeAt(at), 0x01000193) >>> 0;
  const suffix = hash.toString(36);
  return `${id.slice(0, maxIdLength - suffix.length - 1).replace(/[-_]+$/, '')}-${suffix}`;
}

const applicationReceived = builtInEmailTemplates[0]!;
const stageChange = builtInEmailTemplates[1]!;

// ---- Whether a workflow already does it ----------------------------------------------------------------------------------

const triggerKey = (automation: Automation): string => {
  const { trigger } = automation;
  return [trigger.type, trigger.stageId ?? '', trigger.afterHours ?? '', trigger.questionnaireId ?? ''].join('|');
};

const ruleKey = (rule: AutomationCondition): string =>
  JSON.stringify([rule.fact, rule.operator, Boolean(rule.negate), rule.value?.toLowerCase() ?? '', [...(rule.values ?? [])].map((value) => value.toLowerCase()).toSorted()]);

const conditionsKey = (automation: Automation): string =>
  JSON.stringify([automation.conditions?.match ?? 'All', (automation.conditions?.rules ?? []).map(ruleKey).toSorted()]);

/** What an action does, by the argument that tells two of its kind apart. */
const actionKey = (action: AutomationAction): string => {
  switch (action.type) {
    case 'SendEmail':
      return `SendEmail|${action.emailTemplate ?? ''}`;
    case 'AddTag':
      return `AddTag|${(action.tag ?? '').toLowerCase()}`;
    case 'MoveToStage':
      return `MoveToStage|${action.stageId ?? ''}`;
    case 'SendQuestionnaire':
      return `SendQuestionnaire|${action.questionnaireId ?? ''}`;
    case 'AssignReviewer':
      return `AssignReviewer|${action.assigneeUserId ?? ''}`;
    default:
      return action.type;
  }
};

/**
 * Whether `existing` already does what `wanted` does: the same trigger and conditions, and at least its actions. Extra
 * actions on the existing one do not matter. Names and ids do not either.
 */
export function covers(existing: Automation, wanted: Automation): boolean {
  if (triggerKey(existing) !== triggerKey(wanted) || conditionsKey(existing) !== conditionsKey(wanted)) return false;
  const have = new Set(existing.actions.map(actionKey));
  return wanted.actions.every((action) => have.has(actionKey(action)));
}

/** Whether the workflow has `recipe`: an automation with its id, or one that does the same thing. */
export const hasRecipe = (workflow: HiringWorkflow, recipe: Recipe): boolean =>
  workflow.automations.some((automation) => automation.id === recipe.automation.id || covers(automation, recipe.automation));

// ---- The recipes ---------------------------------------------------------------------------------------------------------

const quoted = (name: string): string => `"${name}"`;

function make(
  key: string,
  group: RecipeGroup,
  title: string,
  line: string,
  automation: Omit<Automation, 'id' | 'name' | 'enabled' | 'allowAutoReject'>,
): Recipe {
  const built: Automation = { id: recipeId(key), name: title, ...automation, enabled: true, allowAutoReject: false };
  return { key, group, title, line, lane: laneOf(built), automation: built };
}

/** Every recipe this template's questions and stages allow, added or not. */
export function allRecipes(workflow: HiringWorkflow): Recipe[] {
  const recipes: Recipe[] = [
    make(
      'thanks',
      'Keep candidates in the loop',
      'Thank people when they apply',
      `When someone applies, send the ${quoted(applicationReceived.name)} email.`,
      {
        trigger: { type: 'ApplicationSubmitted' },
        conditions: null,
        actions: [{ type: 'SendEmail', emailTemplate: applicationReceived.id }],
      },
    ),
  ];

  // The first stage is where an application starts, so nobody moves into it; the stages that end a candidacy send their own emails.
  workflow.stages.forEach((stage, index) => {
    if (index === 0 || stage.kind === 'Applied' || isTerminal(stage.kind)) return;
    recipes.push(
      make(
        `reaches-${stage.id}`,
        'Keep candidates in the loop',
        `Tell people when they reach ${stage.name}`,
        `When someone moves into ${stage.name}, send the ${quoted(stageChange.name)} email.`,
        {
          trigger: { type: 'StageEntered', stageId: stage.id },
          conditions: null,
          actions: [{ type: 'SendEmail', emailTemplate: stageChange.id }],
        },
      ),
    );
  });

  // A knockout is an objective must-have the candidate declares. The recipe asks the team to look before anyone is turned down.
  for (const question of workflow.questionnaire.questions) {
    if (question.type !== 'YesNo' || !question.knockout) continue;
    const answer = question.knockout.disqualifyingAnswer ? 'yes' : 'no';
    const word = question.knockout.disqualifyingAnswer ? 'Yes' : 'No';
    const label = question.label || question.id;
    recipes.push(
      make(
        `knockout-${question.id}`,
        'Check the must-haves',
        `Review anyone who fails ${quoted(label)}`,
        `If ${quoted(label)} is ${word}, the team reviews before anyone is turned down.`,
        {
          trigger: { type: 'ApplicationSubmitted' },
          conditions: { match: 'All', rules: [{ fact: `answers.${question.id}`, operator: 'EqualTo', value: answer }] },
          actions: [{ type: 'QueueForRejectionReview', reason: `${label}: answered ${word}` }],
        },
      ),
    );
  }

  recipes.push(
    make(
      'look-closer',
      'Look closer when the checks suggest it',
      'Ask the team to look closer',
      'When the checks finish and Verification is Review or Verify, ask the team to review and add the tag "Check closely".',
      {
        trigger: { type: 'VerificationAssessed' },
        conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] }] },
        actions: [{ type: 'AssignReviewer' }, { type: 'AddTag', tag: 'Check closely' }],
      },
    ),
    make(
      'verify-first',
      'Look closer when the checks suggest it',
      'Tag anyone the checks say to verify',
      'When the checks finish and Verification is Verify, add the tag "Verify first".',
      {
        trigger: { type: 'VerificationAssessed' },
        conditions: { match: 'All', rules: [{ fact: 'verification.authenticity', operator: 'EqualTo', value: 'VerificationRecommended' }] },
        actions: [{ type: 'AddTag', tag: 'Verify first' }],
      },
    ),
  );
  return recipes;
}

/** The workflow with the recipe's automation added at the end. */
export const withRecipe = (workflow: HiringWorkflow, recipe: Recipe): HiringWorkflow => ({
  ...workflow,
  automations: [...workflow.automations, recipe.automation],
});

/**
 * The recipes still on offer: the ones the workflow does not have, and that add no refused step anywhere (a tag a check
 * adds can tangle another automation that reads tags, and the guardrail says so before the validator does). With a
 * `lane`, only the recipes that hang under that stage.
 */
export function recipesFor(workflow: HiringWorkflow, lane?: string | null): Recipe[] {
  return allRecipes(workflow).filter(
    (recipe) => (lane === undefined || lane === null || recipe.lane === lane) && !hasRecipe(workflow, recipe) && !breaksWorkflow(workflow, withRecipe(workflow, recipe)),
  );
}

/** Offered recipes under their group, in the groups' order, leaving out a group with none. */
export function groupedRecipes(workflow: HiringWorkflow, lane?: string | null): { group: RecipeGroup; recipes: Recipe[] }[] {
  const offered = recipesFor(workflow, lane);
  return recipeGroups.map((group) => ({ group, recipes: offered.filter((recipe) => recipe.group === group) })).filter((entry) => entry.recipes.length > 0);
}
