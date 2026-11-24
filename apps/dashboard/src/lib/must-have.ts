// A must-have answer and the automation behind it (workflow-engine.md section 4, Forms): marking a yes-or-no question of the
// application form adds the recipe that sends whoever fails it to Rejection review, as a form's send moment writes its own
// automation, and unmarking it takes that automation away. Only an automation that is still exactly what the recipe wrote is
// rewritten or removed; one a person has edited is left alone and named beside the question. Nothing here turns anyone down.
// No Svelte runes, so the rules are tested without a page.
import type { Automation, HiringWorkflow, Question } from '@screenhire/api-client';
import { covers, mustHaveRecipe, recipeId } from '#lib/automation-recipes.js';

/** What happens to the answer that fails a must-have, as the question's own line says it. */
export type MustHaveState =
  /** It goes to Rejection review. `untouched` when the automation is still the recipe's own. */
  | { kind: 'review'; automation: Automation; untouched: boolean }
  /** Other automations that are on read the answer. */
  | { kind: 'read'; automations: Automation[] }
  /** Only automations that are switched off read it. */
  | { kind: 'off'; automations: Automation[] }
  | { kind: 'nothing' };

const isOn = (automation: Automation): boolean => automation.enabled !== false;

const reads = (automation: Automation, questionId: string): boolean =>
  automation.conditions?.rules.some((rule) => rule.fact === `answers.${questionId}`) ?? false;

/** Ends the candidacy without a person: the steps the validator allows only behind the automation's own opt-in. */
const turnsDown = (workflow: HiringWorkflow, automation: Automation): boolean =>
  automation.actions.some((action) => {
    if (action.type === 'Reject') return true;
    const kind = action.type === 'MoveToStage' ? workflow.stages.find((stage) => stage.id === action.stageId)?.kind : undefined;
    return kind === 'Rejected' || kind === 'Withdrawn';
  });

// A saved draft comes back with every member the API has, so an automation is compared without the ones that say nothing:
// nulls, false, and empty text and lists.
function plain(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(plain);
  if (!value || typeof value !== 'object') return value;
  return Object.fromEntries(
    Object.entries(value)
      .map(([key, member]) => [key, plain(member)] as const)
      .filter(([, member]) => member !== null && member !== undefined && member !== false && member !== '' && !(Array.isArray(member) && member.length === 0))
      .toSorted(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)),
  );
}

const contentOf = (automation: Automation): string => JSON.stringify(plain({ ...automation, id: null, enabled: null, off: !isOn(automation) }));

/** Whether `automation` is still exactly what the recipe for `question` writes, whatever its id. */
export function isUntouched(automation: Automation, question: Question): boolean {
  const recipe = mustHaveRecipe(question);
  return recipe !== null && contentOf(automation) === contentOf(recipe.automation);
}

/**
 * The workflow's automations after the application form's questions changed from `before` to what they are now: a question
 * that became a must-have (a copy of one too) gains the recipe's automation unless some automation, on or off, reads its
 * answer already; one that is no longer a must-have, or is gone, loses the automation the recipe wrote; and one whose text or
 * answer changed has that automation written again. The same list when nothing changes.
 */
export function syncMustHaves(workflow: HiringWorkflow, before: readonly Question[]): Automation[] {
  const { questions } = workflow.questionnaire;
  let automations = workflow.automations;

  for (const old of before) {
    const was = mustHaveRecipe(old);
    if (!was || !automations.some((automation) => isUntouched(automation, old))) continue;
    const question = questions.find((candidate) => candidate.id === old.id);
    const now = question ? mustHaveRecipe(question) : null;
    if (now && contentOf(now.automation) === contentOf(was.automation)) continue;
    automations = automations.flatMap((automation) => (!isUntouched(automation, old) ? [automation] : now ? [{ ...now.automation, id: automation.id }] : []));
  }

  for (const question of questions) {
    const recipe = mustHaveRecipe(question);
    if (!recipe || before.some((old) => old.id === question.id && mustHaveRecipe(old)) || automations.some((automation) => reads(automation, question.id))) continue;
    const taken = new Set(automations.map((automation) => automation.id));
    let { id } = recipe.automation;
    for (let n = 2; taken.has(id); n++) id = recipeId(`again${n}-${recipe.key}`);
    automations = [...automations, { ...recipe.automation, id }];
  }

  return automations;
}

/** What happens to the answer that fails `question`'s must-have. */
export function mustHaveState(workflow: HiringWorkflow, question: Question): MustHaveState {
  const readers = workflow.automations.filter((automation) => reads(automation, question.id));
  const on = readers.filter(isOn);
  // The recipe runs when someone applies, so it can only answer for a question the application form asks.
  const asked = workflow.questionnaire.questions.some((candidate) => candidate.id === question.id);
  const recipe = asked ? mustHaveRecipe(question) : null;
  if (recipe && !on.some((automation) => turnsDown(workflow, automation))) {
    const untouched = on.find((automation) => isUntouched(automation, question));
    const reviews = untouched ?? on.find((automation) => covers(automation, recipe.automation));
    if (reviews) return { kind: 'review', automation: reviews, untouched: reviews === untouched };
  }
  if (on.length > 0) return { kind: 'read', automations: on };
  return readers.length > 0 ? { kind: 'off', automations: readers } : { kind: 'nothing' };
}

/**
 * The automations that still send this answer to Rejection review, or turn someone down on it, though the question is not a
 * must-have: one a person edited stays when the must-have is turned off.
 */
export const leftReading = (workflow: HiringWorkflow, question: Question): Automation[] =>
  workflow.automations.filter(
    (automation) =>
      isOn(automation) &&
      reads(automation, question.id) &&
      (turnsDown(workflow, automation) || automation.actions.some((action) => action.type === 'QueueForRejectionReview')),
  );
