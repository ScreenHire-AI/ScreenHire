import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { allRecipes, covers, groupedRecipes, hasRecipe, recipeGroups, recipeId, recipesFor, withRecipe } from './automation-recipes.js';
import { checkDrivenIds, checkSafeActions, violations } from './automation-sentence.js';

const workflow = (automations: Automation[] = []): HiringWorkflow => ({
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'phone', name: 'Phone call', kind: 'Screening' },
    { id: 'trial', name: 'Trial shift', kind: 'Assessment' },
    { id: 'offer', name: 'Offer', kind: 'Offer' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
  ],
  questionnaire: {
    questions: [
      { id: 'right_to_work', type: 'YesNo', label: 'Right to work in the UK?', required: true, knockout: { disqualifyingAnswer: false, reason: null } },
      { id: 'licence', type: 'YesNo', label: 'Full driving licence?' },
      { id: 'felony', type: 'YesNo', label: 'Ever convicted?', knockout: { disqualifyingAnswer: true, reason: null } },
      { id: 'notes', type: 'ShortText', label: 'Anything else' },
    ],
  },
  verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations,
});

const automation = (id: string, trigger: Automation['trigger'], actions: Automation['actions'], rules: NonNullable<Automation['conditions']>['rules'] = []): Automation => ({
  id,
  name: id,
  trigger,
  conditions: rules.length > 0 ? { match: 'All', rules } : null,
  actions,
});

const titles = (flow: HiringWorkflow) => recipesFor(flow).map((recipe) => recipe.title);

describe('the recipes', () => {
  it('are written for the template\'s own questions and stages, in three groups', () => {
    const groups = groupedRecipes(workflow());

    expect(groups.map((entry) => entry.group)).toEqual([...recipeGroups]);
    expect(groups[0]!.recipes.map((recipe) => recipe.title)).toEqual([
      'Thank people when they apply',
      'Tell people when they reach Phone call',
      'Tell people when they reach Trial shift',
      'Tell people when they reach Offer',
    ]);
    expect(groups[1]!.recipes.map((recipe) => recipe.title)).toEqual(['Review anyone who fails "Right to work in the UK?"', 'Review anyone who fails "Ever convicted?"']);
    expect(groups[2]!.recipes.map((recipe) => recipe.title)).toEqual(['Ask the team to look closer', 'Tag anyone the checks say to verify']);
  });

  it('are one line each, and name no person', () => {
    for (const recipe of allRecipes(workflow())) {
      expect(recipe.line, recipe.key).not.toContain('\n');
      expect(recipe.line, recipe.key).toMatch(/\.$/);
      expect(recipe.line, recipe.key).not.toMatch(/Dana|Avery|\b[A-Z][a-z]+ [A-Z][a-z]+\b(?<!Right to work|Phone call|Trial shift)/);
      expect(JSON.stringify(recipe.automation), recipe.key).not.toContain('assigneeUserId');
    }
  });

  it('read a knockout as its disqualifying answer and ask the team to look before anyone is turned down', () => {
    const recipe = allRecipes(workflow()).find((entry) => entry.key === 'knockout-felony')!;

    expect(recipe.line).toBe('If "Ever convicted?" is Yes, the team reviews before anyone is turned down.');
    expect(recipe.automation).toMatchObject({
      id: 'recipe-knockout-felony',
      trigger: { type: 'ApplicationSubmitted' },
      conditions: { match: 'All', rules: [{ fact: 'answers.felony', operator: 'EqualTo', value: 'yes' }] },
      actions: [{ type: 'QueueForRejectionReview', reason: 'Ever convicted?: answered Yes' }],
    });
  });

  it('put a stage\'s recipe in that stage\'s lane, and the rest in the whole workflow\'s', () => {
    const recipes = allRecipes(workflow());

    expect(recipes.find((recipe) => recipe.key === 'reaches-trial')!.lane).toBe('trial');
    expect(recipes.find((recipe) => recipe.key === 'thanks')!.lane).toBeNull();
    expect(recipesFor(workflow(), 'trial').map((recipe) => recipe.key)).toEqual(['reaches-trial']);
    expect(recipesFor(workflow(), null).length).toBe(recipesFor(workflow()).length);
  });

  it('never email a stage that ends the application, or the one every application starts in', () => {
    const keys = allRecipes(workflow()).map((recipe) => recipe.key);

    expect(keys).not.toContain('reaches-applied');
    expect(keys).not.toContain('reaches-hired');
    expect(keys).not.toContain('reaches-rejected');
  });

  it('never reject, move to a stage that ends the candidacy, or send anything after a check', () => {
    for (const recipe of allRecipes(workflow())) {
      const types = recipe.automation.actions.map((action) => action.type);
      expect(types, recipe.key).not.toContain('Reject');
      const moves = recipe.automation.actions.filter((action) => action.type === 'MoveToStage').map((action) => action.stageId);
      expect(moves.filter((id) => id === 'rejected' || id === 'hired'), recipe.key).toEqual([]);
    }
    const checks = allRecipes(workflow()).filter((recipe) => recipe.group === 'Look closer when the checks suggest it');
    expect(checks).toHaveLength(2);
    for (const recipe of checks) {
      expect(recipe.automation.actions.every((action) => checkSafeActions.includes(action.type)), recipe.key).toBe(true);
      expect(checkDrivenIds(withRecipe(workflow(), recipe)).has(0), recipe.key).toBe(true);
    }
  });

  it('add no refused step, one at a time or all together', () => {
    let flow = workflow();
    expect(violations(flow).size).toBe(0);
    for (const recipe of recipesFor(flow)) {
      expect(violations(withRecipe(flow, recipe)).size, recipe.key).toBe(0);
    }
    for (let guard = 0; guard < 20 && recipesFor(flow).length > 0; guard++) flow = withRecipe(flow, recipesFor(flow)[0]!);
    expect(violations(flow).size).toBe(0);
    expect(recipesFor(flow)).toEqual([]);
  });

  it('use ids that are unique and stable, so a removed one comes back', () => {
    const recipes = allRecipes(workflow());
    expect(new Set(recipes.map((recipe) => recipe.automation.id)).size).toBe(recipes.length);
    expect(recipes[0]!.automation.id).toBe(recipeId('thanks'));

    const added = withRecipe(workflow(), recipes[0]!);
    expect(titles(added)).not.toContain('Thank people when they apply');
    expect(titles({ ...added, automations: [] })).toContain('Thank people when they apply');
  });
});

describe('an added recipe is hidden', () => {
  it('when the workflow already does the same thing under another id and name', () => {
    const own = automation('welcome', { type: 'ApplicationSubmitted' }, [{ type: 'AddTag', tag: 'new' }, { type: 'SendEmail', emailTemplate: 'application-received' }]);

    expect(titles(workflow([own]))).not.toContain('Thank people when they apply');
    expect(titles(workflow())).toContain('Thank people when they apply');
  });

  it('only when the trigger, the conditions and the email are all the same', () => {
    const other = automation('x', { type: 'ApplicationSubmitted' }, [{ type: 'SendEmail', emailTemplate: 'stage-change' }]);
    const elsewhere = automation('y', { type: 'StageEntered', stageId: 'offer' }, [{ type: 'SendEmail', emailTemplate: 'stage-change' }]);

    expect(titles(workflow([other]))).toContain('Thank people when they apply');
    expect(titles(workflow([elsewhere]))).not.toContain('Tell people when they reach Offer');
    expect(titles(workflow([elsewhere]))).toContain('Tell people when they reach Trial shift');
  });

  it('for a starting point\'s own thank-you, knockout review and stage email, so one press never sends twice', () => {
    const startingPoint = workflow([
      automation('thanks', { type: 'ApplicationSubmitted' }, [{ type: 'SendEmail', emailTemplate: 'application-received' }]),
      automation('rtw', { type: 'ApplicationSubmitted' }, [{ type: 'QueueForRejectionReview', reason: 'No right to work' }], [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }]),
      automation('trial', { type: 'StageEntered', stageId: 'trial' }, [{ type: 'SendEmail', emailTemplate: 'stage-change' }]),
    ]);

    const left = titles(startingPoint);
    expect(left).not.toContain('Thank people when they apply');
    expect(left).not.toContain('Review anyone who fails "Right to work in the UK?"');
    expect(left).not.toContain('Tell people when they reach Trial shift');
    expect(left).toContain('Review anyone who fails "Ever convicted?"');
  });

  it('by whether an automation covers it, whatever else that automation does', () => {
    const wanted = allRecipes(workflow()).find((recipe) => recipe.key === 'thanks')!;
    const same = automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'SendEmail', emailTemplate: 'application-received' }, { type: 'NotifyTeam', note: 'hi' }]);

    expect(covers(same, wanted.automation)).toBe(true);
    expect(covers({ ...same, conditions: { match: 'All', rules: [{ fact: 'application.tags', operator: 'Exists' }] } }, wanted.automation)).toBe(false);
    expect(hasRecipe(workflow([same]), wanted)).toBe(true);
  });
});

describe('the recipes after a check', () => {
  it('leave out a tag recipe that would tangle another automation reading tags', () => {
    const reader = automation('reader', { type: 'ApplicationSubmitted' }, [{ type: 'SendEmail', emailTemplate: 'stage-change' }], [
      { fact: 'application.tags', operator: 'Contains', value: 'check closely' },
    ]);

    const left = titles(workflow([reader]));
    expect(left).not.toContain('Ask the team to look closer');
    expect(left).toContain('Tag anyone the checks say to verify');
  });

  it('leave out a recipe when a check already routes into a stage with an email', () => {
    const negated = automation('negated', { type: 'ApplicationSubmitted' }, [{ type: 'SendEmail', emailTemplate: 'stage-change' }], [
      { fact: 'application.tags', operator: 'Contains', value: 'referral', negate: true },
    ]);

    expect(titles(workflow([negated]))).not.toContain('Ask the team to look closer');
    expect(titles(workflow([negated]))).not.toContain('Tag anyone the checks say to verify');
  });
});
