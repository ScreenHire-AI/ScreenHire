import type { Automation, HiringWorkflow, Question } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { hasRecipe, mustHaveRecipe, recipesFor } from './automation-recipes.js';
import { isUntouched, leftReading, mustHaveState, syncMustHaves } from './must-have.js';

const licence: Question = { id: 'licence', type: 'YesNo', label: 'Do you hold a licence?', required: true };
const mustHave: Question = { ...licence, knockout: { disqualifyingAnswer: false, reason: null } };

const workflow = (questions: Question[], automations: Automation[] = []): HiringWorkflow => ({
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
  ],
  questionnaire: { questions },
  automations,
});

/** The workflow after its questions change from what they are to `questions`, as the form builder changes them. */
const changed = (flow: HiringWorkflow, questions: Question[]): HiringWorkflow => {
  const next = { ...flow, questionnaire: { questions } };
  return { ...next, automations: syncMustHaves(next, flow.questionnaire.questions) };
};

const on = () => changed(workflow([licence]), [mustHave]);

/** An automation as the API sends a saved one back: every member there, the unused ones null, false or empty. */
const saved = (automation: Automation): Automation => ({
  ...automation,
  trigger: { ...automation.trigger, stageId: null, afterHours: null, questionnaireId: null },
  conditions: automation.conditions && { ...automation.conditions, rules: automation.conditions.rules.map((rule) => ({ ...rule, values: null, negate: false })) },
  actions: automation.actions.map((action) => ({ ...action, emailTemplate: null, stageId: null, tag: null, note: null, interviewerUserIds: null })),
});

const reader = (id: string, actions: Automation['actions'], value = 'no'): Automation => ({
  id,
  name: `Reads it: ${id}`,
  trigger: { type: 'ApplicationSubmitted' },
  conditions: { match: 'All', rules: [{ fact: 'answers.licence', operator: 'EqualTo', value }] },
  actions,
  enabled: true,
  allowAutoReject: false,
});

describe('turning a must-have on', () => {
  it('adds the review recipe, which only queues for a person', () => {
    const [added, ...rest] = on().automations;

    expect(rest).toEqual([]);
    expect(added).toEqual(mustHaveRecipe(mustHave)!.automation);
    expect(added).toMatchObject({ actions: [{ type: 'QueueForRejectionReview' }], enabled: true, allowAutoReject: false });
    expect(mustHaveState(on(), mustHave)).toEqual({ kind: 'review', automation: added, untouched: true });
    // The same automation the Add an automation list offers, so it is never offered beside it.
    expect(recipesFor(on()).map((recipe) => recipe.key)).not.toContain('knockout-licence');
  });

  it('adds it once', () => {
    const twice = changed(changed(on(), [{ ...mustHave, helpText: 'A full one' }]), [mustHave]);

    expect(twice.automations).toHaveLength(1);
    expect(hasRecipe(twice, mustHaveRecipe(mustHave)!)).toBe(true);
  });

  it('adds nothing where an automation reads the answer already, on or off', () => {
    const tags = reader('tags', [{ type: 'AddTag', tag: 'no licence' }]);
    const off = { ...tags, enabled: false };

    const read = changed(workflow([licence], [tags]), [mustHave]);
    expect(read.automations).toEqual([tags]);
    expect(mustHaveState(read, mustHave)).toEqual({ kind: 'read', automations: [tags] });

    const unread = changed(workflow([licence], [off]), [mustHave]);
    expect(unread.automations).toEqual([off]);
    expect(mustHaveState(unread, mustHave)).toEqual({ kind: 'off', automations: [off] });
  });

  it('gives the recipe another id when an edited automation kept its own', () => {
    const edited: Automation = { ...on().automations[0]!, conditions: null, name: 'Mine now' };
    const next = changed(workflow([licence], [edited]), [mustHave]);

    expect(next.automations[0]).toEqual(edited);
    expect(next.automations).toHaveLength(2);
    expect(next.automations[1]!.id).not.toBe(edited.id);
    expect(mustHaveState(next, mustHave)).toMatchObject({ kind: 'review', untouched: true });
  });

  it('gives a copy of a must-have its own', () => {
    const copy: Question = { ...mustHave, id: 'licence_2' };
    const next = changed(on(), [mustHave, copy]);

    expect(next.automations.map((automation) => automation.conditions!.rules[0]!.fact)).toEqual(['answers.licence', 'answers.licence_2']);
  });

  it("stays inside the validator's limits for the longest question", () => {
    const { name, actions } = mustHaveRecipe({ ...mustHave, label: 'q'.repeat(500) })!.automation;

    expect(name.length).toBeLessThanOrEqual(120);
    expect(actions[0]!.reason!.length).toBeLessThanOrEqual(500);
    expect(actions[0]!.reason).toMatch(/: answered No$/);
  });

  it('never cuts an emoji in half, which the API cannot store', () => {
    const truck = String.fromCodePoint(0x1f69a);
    const { name } = mustHaveRecipe({ ...mustHave, label: `${'q'.repeat(93)}${truck} licence?` })!.automation;

    expect(name).toBe(`Review anyone who fails "${'q'.repeat(93)}"`);
  });
});

describe('turning it off', () => {
  it('takes away an automation that is still exactly what the recipe wrote, a saved one too', () => {
    expect(changed(on(), [licence]).automations).toEqual([]);

    const reloaded = { ...on(), automations: on().automations.map(saved) };
    expect(isUntouched(reloaded.automations[0]!, mustHave)).toBe(true);
    expect(changed(reloaded, [licence]).automations).toEqual([]);
  });

  it.each<[string, (automation: Automation) => Automation]>([
    ['another name', (automation) => ({ ...automation, name: 'Licence check' })],
    ['another step', (automation) => ({ ...automation, actions: [...automation.actions, { type: 'AddTag', tag: 'no licence' }] })],
    ['another reason', (automation) => ({ ...automation, actions: [{ ...automation.actions[0]!, reason: 'No licence' }] })],
    ['another trigger', (automation) => ({ ...automation, trigger: { type: 'StageEntered', stageId: 'applied' } })],
    ['its own opt-in', (automation) => ({ ...automation, allowAutoReject: true })],
    ['being switched off', (automation) => ({ ...automation, enabled: false })],
  ])('leaves one a person gave %s', (_, edit) => {
    const edited = edit(on().automations[0]!);
    const flow = { ...on(), automations: [edited] };

    expect(changed(flow, [licence]).automations).toEqual([edited]);
    expect(changed(flow, [{ ...mustHave, knockout: { disqualifyingAnswer: true, reason: null } }]).automations).toEqual([edited]);
    expect(changed(flow, [{ ...mustHave, label: 'Licence?' }]).automations).toEqual([edited]);
  });

  it('names an edited one that still queues for review, and not one that only tags', () => {
    const edited = { ...on().automations[0]!, name: 'Licence check' };
    const tags = reader('tags', [{ type: 'AddTag', tag: 'no licence' }]);

    expect(leftReading(workflow([licence], [edited, tags]), licence)).toEqual([edited]);
    expect(leftReading(workflow([licence], [{ ...edited, enabled: false }]), licence)).toEqual([]);
  });

  it('is deleting the question or changing its answer kind, too', () => {
    expect(changed(on(), []).automations).toEqual([]);
    expect(changed(on(), [{ id: 'licence', type: 'ShortText', label: licence.label }]).automations).toEqual([]);
  });

  it('leaves every other automation alone', () => {
    const other: Automation = { id: 'thanks', name: 'Thanks', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'SendEmail', emailTemplate: 'application-received' }] };
    const flow = { ...on(), automations: [other, ...on().automations] };

    expect(changed(flow, [licence]).automations).toEqual([other]);
  });
});

describe('changing the question', () => {
  it('moves the automation to the new failing answer, under its own id', () => {
    const flow = { ...on(), automations: on().automations.map((automation) => saved({ ...automation, id: 'kept' })) };
    const next = changed(flow, [{ ...mustHave, knockout: { disqualifyingAnswer: true, reason: null } }]);

    expect(next.automations).toHaveLength(1);
    expect(next.automations[0]).toMatchObject({
      id: 'kept',
      conditions: { rules: [{ fact: 'answers.licence', operator: 'EqualTo', value: 'yes' }] },
      actions: [{ type: 'QueueForRejectionReview', reason: 'Do you hold a licence?: answered Yes' }],
    });
  });

  it('carries its words into the automation, so a rename never makes it look edited', () => {
    const renamed = { ...mustHave, label: 'Do you hold a forklift licence?' };
    const next = changed(on(), [renamed]);

    expect(next.automations[0]!.name).toBe('Review anyone who fails "Do you hold a forklift licence?"');
    expect(changed(next, [{ ...renamed, knockout: null }]).automations).toEqual([]);
  });

  it('changes nothing for a change that is not about the must-have', () => {
    const flow = on();
    const questions = [{ ...mustHave, required: false, helpText: 'A full one' }];
    const next = { ...flow, questionnaire: { questions } };

    expect(syncMustHaves(next, flow.questionnaire.questions)).toBe(flow.automations);
  });
});

describe('what happens to the failing answer', () => {
  it('is nothing until an automation reads it', () => {
    expect(mustHaveState(workflow([mustHave]), mustHave)).toEqual({ kind: 'nothing' });
  });

  it("is Rejection review for a starting point's own automation, named as a person's", () => {
    const own = reader('right_to_work_review', [{ type: 'QueueForRejectionReview', reason: 'No licence' }]);

    expect(mustHaveState(workflow([mustHave], [own]), mustHave)).toEqual({ kind: 'review', automation: own, untouched: false });
  });

  it('never says a person decides when another automation turns them down by itself', () => {
    const rejects = { ...reader('reject', [{ type: 'Reject', reason: 'No licence' }]), allowAutoReject: true };
    const moves = { ...reader('move', [{ type: 'MoveToStage', stageId: 'rejected' }]) };

    for (const ends of [rejects, moves]) {
      const flow = { ...on(), automations: [...on().automations, ends] };
      expect(mustHaveState(flow, mustHave), ends.id).toEqual({ kind: 'read', automations: flow.automations });
    }
  });

  it('is never the recipe on another form, which is answered after applying', () => {
    const flow: HiringWorkflow = {
      ...workflow([], [reader('early', [{ type: 'QueueForRejectionReview', reason: 'No licence' }])]),
      followUpQuestionnaires: [{ id: 'more', name: 'More', questions: [mustHave] }],
    };

    expect(mustHaveState(flow, mustHave)).toEqual({ kind: 'read', automations: flow.automations });
  });
});
