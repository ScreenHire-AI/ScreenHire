import type { Automation, AutomationAction, AutomationCondition, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  automationLine,
  blanksOf,
  breaks,
  changeTrigger,
  checkDrivenIds,
  checkGuardrailLine,
  leftOutLine,
  readSentence,
  relationChoices,
  ruleHoldsForTags,
  sentenceOf,
  thenChoices,
  triggerTitle,
  violations,
  waitLabel,
  withRelation,
  withValues,
  type Blank,
  type ChoiceBlank,
  type SentenceContext,
  type TextBlank,
} from './automation-sentence.js';
import { factsFor, triggerNeedsStage, triggerTypes } from './workflow.js';

const stages: HiringWorkflow['stages'] = [
  { id: 'applied', name: 'Applied', kind: 'Applied' },
  { id: 'screening', name: 'Phone call', kind: 'Screening', slaDays: 2 },
  { id: 'trial', name: 'Trial shift', kind: 'Assessment' },
  { id: 'interview', name: 'Interview', kind: 'Interview', scorecardTemplateId: 'interview' },
  { id: 'offer', name: 'Offer', kind: 'Offer' },
  { id: 'hired', name: 'Hired', kind: 'Hired' },
  { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
];

const ctx: SentenceContext = {
  emailTemplates: [
    { id: 'application-received', name: 'Application received' },
    { id: 'stage-change', name: 'Moved to the next step' },
  ],
  reviewers: [{ userId: 'u-1', displayName: 'Dana Whitfield' }],
  teamKnown: true,
};

const flow = (automations: Automation[], extra: Partial<HiringWorkflow> = {}): HiringWorkflow => ({
  stages,
  questionnaire: {
    questions: [
      { id: 'right_to_work', type: 'YesNo', label: 'Right to work', required: true, knockout: { disqualifyingAnswer: false, reason: null } },
      {
        id: 'shift',
        type: 'SingleChoice',
        label: 'Shift',
        options: [
          { id: 'days', label: 'Days' },
          { id: 'nights', label: 'Nights' },
        ],
      },
      {
        id: 'tools',
        type: 'MultiChoice',
        label: 'Tools',
        options: [
          { id: 'saw', label: 'Saw' },
          { id: 'drill', label: 'Drill' },
        ],
      },
      { id: 'notes', type: 'ShortText', label: 'Anything else' },
    ],
  },
  verificationPolicy: { aiUse: 'AllowedWithDisclosure' },
  automations,
  ...extra,
});

const automation = (
  id: string,
  trigger: Automation['trigger'],
  actions: AutomationAction[],
  rules: AutomationCondition[] = [],
  match: 'All' | 'Any' = 'All',
): Automation => ({ id, name: id, trigger, conditions: rules.length > 0 ? { match, rules } : null, actions });

const mail: AutomationAction = { type: 'SendEmail', emailTemplate: 'stage-change' };
const checkRule: AutomationCondition = { fact: 'verification.authenticity', operator: 'EqualTo', value: 'ReviewSuggested' };

const sentence = (workflow: HiringWorkflow, index = 0) => sentenceOf(workflow, index, ctx);
const blankOf = (workflow: HiringWorkflow, id: string, index = 0): Blank => {
  const found = blanksOf(sentence(workflow, index)).find((blank) => blank.id === id);
  if (!found) throw new Error(`No blank ${id} in: ${readSentence(sentence(workflow, index))}`);
  return found;
};
const choice = (workflow: HiringWorkflow, id: string, index = 0) => blankOf(workflow, id, index) as ChoiceBlank;
const labels = (blank: ChoiceBlank) => blank.choices.map((entry) => entry.label);
const ids = (blank: ChoiceBlank) => blank.choices.map((entry) => entry.id);

describe('the sentence', () => {
  it('reads an automation as one sentence with a blank for each part', () => {
    const workflow = flow([
      automation(
        'review',
        { type: 'ApplicationSubmitted' },
        [{ type: 'AssignReviewer' }, { type: 'AddTag', tag: 'Not eligible' }],
        [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }],
      ),
    ]);

    expect(readSentence(sentence(workflow))).toBe('When someone applies and their answer to Right to work is No, ask the team to review and add the tag Not eligible.');
  });

  it('reads a stage trigger, a wait and a check in plain words', () => {
    expect(readSentence(sentence(flow([automation('a', { type: 'StageEntered', stageId: 'screening' }, [mail])])))).toBe(
      'When someone moves into Phone call, send the email Moved to the next step.',
    );
    expect(readSentence(sentence(flow([automation('a', { type: 'TimeElapsed', stageId: 'screening', afterHours: 48 }, [{ type: 'NotifyTeam', note: 'Chase' }])])))).toBe(
      'When someone has waited 2 days in Phone call, tell the team Chase.',
    );
    expect(
      readSentence(
        sentence(
          flow([
            automation('a', { type: 'VerificationAssessed' }, [{ type: 'AddTag', tag: 'Look' }], [
              { fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] },
            ]),
          ]),
        ),
      ),
    ).toBe('When the checks finish and Verification is Review or Verify, add the tag Look.');
  });

  it('puts each part where the validator looks for its errors, and the first of each where Fix focuses', () => {
    const workflow = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail, { type: 'AddTag', tag: 'x' }], [{ fact: 'application.tags', operator: 'Contains', value: 'x' }])]);

    const blanks = blanksOf(sentence(workflow));
    expect(blanks.find((blank) => blank.id === 'trigger')).toMatchObject({ errorPath: 'automations[0].trigger', focusPath: 'automations[0].trigger' });
    expect(blanks.find((blank) => blank.id === 'rule-0-fact')).toMatchObject({ errorPath: 'automations[0].conditions.rules[0]', focusPath: 'automations[0].conditions' });
    expect(blanks.find((blank) => blank.id === 'action-1-kind')).toMatchObject({ errorPath: 'automations[0].actions[1]', focusPath: 'automations[0].actions[1]' });
  });

  it('ends in the plus blanks that add a condition and a step', () => {
    const blanks = blanksOf(sentence(flow([automation('a', { type: 'ApplicationSubmitted' }, [mail])])));

    expect(blanks.filter((blank) => blank.adds).map((blank) => blank.text)).toEqual(['and if', 'then also']);
  });
});

describe('the trigger blanks', () => {
  const workflow = flow([automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AddTag', tag: 'x' }])]);

  it('offers every trigger the builder runs, in plain words', () => {
    const kinds = choice(workflow, 'trigger');

    expect(labels(kinds)).toEqual([
      'Someone applies',
      'Someone moves into',
      'Someone leaves',
      'Someone has waited',
      'Someone answers',
      'The checks finish',
      'A reviewer decides on',
      'The time limit passes in',
      'An interview is booked',
      'Someone runs it by hand',
    ]);
  });

  it('adds the stage blank a kind needs, starting on the current or the first stage', () => {
    const next = choice(workflow, 'trigger').apply('StageEntered');

    expect(next.trigger).toEqual({ type: 'StageEntered', stageId: 'applied' });
    expect(labels(choice(flow([next]), 'trigger-stage'))).toEqual(stages.map((stage) => stage.name));
  });

  it('asks how long someone has waited in plain steps, and keeps a saved odd wait', () => {
    const waiting = flow([automation('a', { type: 'TimeElapsed', stageId: 'screening', afterHours: 30 }, [mail])]);

    expect(labels(choice(waiting, 'trigger-wait'))).toEqual(['1 day', '30 hours', '2 days', '3 days', '5 days', '1 week']);
    expect(choice(waiting, 'trigger-wait').apply('72').trigger).toEqual({ type: 'TimeElapsed', stageId: 'screening', afterHours: 72 });
    expect(waitLabel(336)).toBe('2 weeks');
  });

  it('lets a reviewer decision or a passed time limit name any stage, or only the stages that can have one', () => {
    const decided = flow([automation('a', { type: 'ReviewerDecision' }, [{ type: 'AssignReviewer' }])]);
    const limited = flow([automation('a', { type: 'SlaBreached' }, [{ type: 'NotifyTeam', note: 'Late' }])]);

    expect(labels(choice(decided, 'trigger-stage'))).toEqual(['Any stage', 'Interview']);
    expect(labels(choice(limited, 'trigger-stage'))).toEqual(['Any stage', 'Phone call']);
    expect(choice(limited, 'trigger-stage').apply('screening').trigger).toEqual({ type: 'SlaBreached', stageId: 'screening' });
    expect(choice(limited, 'trigger-stage').apply('').trigger).toEqual({ type: 'SlaBreached' });
  });

  it('changeTrigger drops what the new kind does not take and keeps a time limit stage that has a limit', () => {
    const base = automation('a', { type: 'StageEntered', stageId: 'screening' }, [mail]);
    const wf = flow([base]);

    expect(changeTrigger(base, wf, 'ApplicationSubmitted').trigger).toEqual({ type: 'ApplicationSubmitted' });
    expect(changeTrigger(base, wf, 'SlaBreached').trigger).toEqual({ type: 'SlaBreached', stageId: 'screening' });
    expect(changeTrigger(base, wf, 'TimeElapsed').trigger).toEqual({ type: 'TimeElapsed', stageId: 'screening', afterHours: 48 });
    expect(changeTrigger({ ...base, trigger: { type: 'StageEntered', stageId: 'trial' } }, wf, 'SlaBreached').trigger).toEqual({ type: 'SlaBreached' });
  });

  it('titles a trigger in the few words a map node holds', () => {
    expect(triggerTitle({ type: 'ApplicationSubmitted' }, workflow)).toBe('Someone applies');
    expect(triggerTitle({ type: 'StageEntered', stageId: 'trial' }, workflow)).toBe('Moves to Trial shift');
    expect(triggerTitle({ type: 'TimeElapsed', stageId: 'screening', afterHours: 48 }, workflow)).toBe('2 days pass in Phone call');
    expect(triggerTitle({ type: 'SlaBreached' }, workflow)).toBe('A time limit passes');
  });
});

describe('the condition blanks', () => {
  const base = automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AddTag', tag: 'x' }], [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }]);
  const workflow = flow([base]);

  it('groups the facts as Their answers, The application and ScreenHire\'s checks, and offers nothing else', () => {
    const fact = choice(workflow, 'rule-0-fact');

    expect(fact.choices.map((entry) => [entry.group, entry.label])).toEqual([
      ['Their answers', 'Right to work'],
      ['Their answers', 'Shift'],
      ['Their answers', 'Tools'],
      ['Their answers', 'Anything else'],
      ['The application', 'Tags'],
      ['The application', 'Where they applied'],
      ['The application', 'Hours in the stage'],
      ["ScreenHire's checks", 'Verification'],
    ]);
  });

  it('never offers an AI-use or accommodation answer: the facts are the template\'s questions and the application\'s own', () => {
    const offered = ids(choice(workflow, 'rule-0-fact'));

    expect(offered.filter((id) => id.startsWith('answers.')).toSorted()).toEqual(['answers.notes', 'answers.right_to_work', 'answers.shift', 'answers.tools']);
    expect(offered.join(' ')).not.toMatch(/accommodat|ai_use|aiUse|telemetry|rating|scorecard/i);
    expect(factsFor(workflow, 'ApplicationSubmitted').map((option) => option.fact).join(' ')).not.toMatch(/accommodat|ai_use|aiUse/i);
  });

  it('offers the reviewer\'s recommendation only on a reviewer decision', () => {
    const decided = flow([automation('a', { type: 'ReviewerDecision' }, [{ type: 'AssignReviewer' }], [{ fact: 'application.tags', operator: 'Exists' }])]);

    expect(labels(choice(decided, 'rule-0-fact'))).toContain("The reviewer's recommendation");
    expect(labels(choice(workflow, 'rule-0-fact'))).not.toContain("The reviewer's recommendation");
  });

  it('leaves AI help out of the choices but still reads a saved condition on it', () => {
    const saved = flow([automation('a', { type: 'VerificationAssessed' }, [{ type: 'AddTag', tag: 'x' }], [{ fact: 'verification.aiAssistance', operator: 'EqualTo', value: 'StrongIndicators' }])]);

    expect(labels(choice(saved, 'rule-0-fact'))).toEqual(expect.arrayContaining(['AI help', 'Verification']));
    expect(readSentence(sentence(saved))).toContain('AI help is Strong signs of AI help');
    const plain = flow([base]);
    expect(labels(choice(plain, 'add-condition'))).not.toContain('AI help');
  });

  it('leaves timing out of a time-limit reminder, which would be skipped for a candidate with an accommodation', () => {
    const limited = flow([automation('a', { type: 'SlaBreached', stageId: 'screening' }, [{ type: 'NotifyTeam', note: 'Late' }])]);

    expect(labels(choice(limited, 'add-condition'))).not.toContain('Hours in the stage');
    expect(labels(choice(workflow, 'add-condition'))).toContain('Hours in the stage');
  });

  it('chooses a fact by starting a fresh rule on it', () => {
    const next = choice(workflow, 'rule-0-fact').apply('answers.shift');

    expect(next.conditions?.rules).toEqual([{ fact: 'answers.shift', operator: 'EqualTo', value: 'days' }]);
  });

  it('offers a fact with a fixed list is, is not, is given and is not given', () => {
    expect(labels(choice(workflow, 'rule-0-relation'))).toEqual(['is', 'is not', 'is given', 'is not given']);
    const tools = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'answers.tools', operator: 'Contains', value: 'saw' }])]);
    expect(labels(choice(tools, 'rule-0-relation'))).toEqual(['includes', 'does not include', 'is given', 'is not given']);
  });

  it('offers a number its comparisons and free text its own', () => {
    const hours = relationChoices(factsFor(workflow).find((option) => option.fact === 'application.hoursInStage'), { fact: 'application.hoursInStage', operator: 'GreaterThan', value: '5' });
    const text = relationChoices(factsFor(workflow).find((option) => option.fact === 'answers.notes'), { fact: 'answers.notes', operator: 'Contains', value: 'a' });

    expect(hours.map((entry) => entry.label)).toEqual(['is', 'is not', 'is more than', 'is at least', 'is less than', 'is at most', 'is given', 'is not given']);
    expect(text.map((entry) => entry.label)).toEqual(['is', 'is not', 'is one of', 'is not one of', 'contains', 'does not contain', 'is given', 'is not given']);
  });

  it('turns a relation into the operator and negation the definition stores', () => {
    const option = factsFor(workflow).find((candidate) => candidate.fact === 'answers.right_to_work');

    expect(withRelation(base.conditions!.rules[0]!, option, 'EqualTo|1')).toEqual({ fact: 'answers.right_to_work', operator: 'EqualTo', negate: true, value: 'no' });
    expect(withRelation(base.conditions!.rules[0]!, option, 'Exists|0')).toEqual({ fact: 'answers.right_to_work', operator: 'Exists' });
    const hours = factsFor(workflow).find((candidate) => candidate.fact === 'application.hoursInStage');
    expect(withRelation({ fact: 'application.hoursInStage', operator: 'EqualTo', value: '24' }, hours, 'GreaterThanOrEqual|0')).toEqual({
      fact: 'application.hoursInStage',
      operator: 'GreaterThanOrEqual',
      value: '24',
    });
  });

  it('ticks values: one stores the relation, two or more store In', () => {
    const option = factsFor(workflow).find((candidate) => candidate.fact === 'answers.shift')!;
    const rule: AutomationCondition = { fact: 'answers.shift', operator: 'EqualTo', value: 'days' };

    expect(withValues(rule, option, ['nights'])).toEqual({ fact: 'answers.shift', operator: 'EqualTo', value: 'nights' });
    expect(withValues(rule, option, ['days', 'nights'])).toEqual({ fact: 'answers.shift', operator: 'In', values: ['days', 'nights'] });
    expect(withValues({ ...rule, negate: true }, option, ['days', 'nights'])).toEqual({ fact: 'answers.shift', operator: 'In', negate: true, values: ['days', 'nights'] });
    const tools = factsFor(workflow).find((candidate) => candidate.fact === 'answers.tools')!;
    expect(withValues({ fact: 'answers.tools', operator: 'Contains', value: 'saw' }, tools, ['drill'])).toEqual({ fact: 'answers.tools', operator: 'Contains', value: 'drill' });
  });

  it('shows a yes or no as one choice and every other list as ticks', () => {
    const shift = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'answers.shift', operator: 'In', values: ['days', 'nights'] }])]);

    expect(blankOf(workflow, 'rule-0-value').kind).toBe('choice');
    expect(blankOf(shift, 'rule-0-value')).toMatchObject({ kind: 'multi', selected: ['days', 'nights'], text: 'Days or Nights' });
    const unticked = (blankOf(shift, 'rule-0-value') as { apply: (ids: string[]) => Automation }).apply([]);
    expect(unticked).toEqual(shift.automations[0]);
  });

  it('asks free text and numbers for a typed value, and a list of words with commas', () => {
    const notes = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'answers.notes', operator: 'In', values: ['a', 'b'] }])]);
    const hours = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'application.hoursInStage', operator: 'GreaterThan', value: '24' }])]);

    expect(blankOf(notes, 'rule-0-value')).toMatchObject({ kind: 'text', value: 'a, b', hint: 'Separated by commas.' });
    expect((blankOf(notes, 'rule-0-value') as { apply: (value: string) => Automation }).apply('x, y ,').conditions?.rules[0]).toEqual({ fact: 'answers.notes', operator: 'In', values: ['x', 'y'] });
    expect(blankOf(hours, 'rule-0-value')).toMatchObject({ kind: 'text', numeric: true });
    expect((blankOf(hours, 'rule-0-value') as { apply: (value: string) => Automation }).apply('48').conditions?.rules[0]).toEqual({ fact: 'application.hoursInStage', operator: 'GreaterThan', value: '48' });
  });

  it('has no value blank for is given', () => {
    const given = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'answers.notes', operator: 'Exists' }])]);

    expect(blanksOf(sentence(given)).some((blank) => blank.id === 'rule-0-value')).toBe(false);
    expect(readSentence(sentence(given))).toContain('their answer to Anything else is given');
  });

  it('joins two conditions with and or or, and says one of when any holds', () => {
    const two = [
      { fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' },
      { fact: 'application.tags', operator: 'Contains', value: 'referral' },
    ] satisfies AutomationCondition[];
    const all = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail], two)]);
    const any = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail], two, 'Any')]);

    expect(readSentence(sentence(all))).toContain('and their answer to Right to work is No and their tags includes referral');
    expect(readSentence(sentence(any))).toContain('and one of their answer to Right to work is No or their tags includes referral');
    expect(choice(all, 'match').apply('Any').conditions?.match).toBe('Any');
  });

  it('adds a condition by naming what to check, and removes one from its own list', () => {
    const added = choice(workflow, 'add-condition').apply('application.tags');
    expect(added.conditions?.rules).toEqual([
      { fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' },
      { fact: 'application.tags', operator: 'Contains', value: '' },
    ]);
    expect(choice(workflow, 'rule-0-fact').remove?.apply().conditions).toBeNull();
  });
});

describe('the step blanks', () => {
  const workflow = flow([automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AddTag', tag: 'x' }])]);

  it('offers every kind of step except a rejection, and a questionnaire only when there is one', () => {
    expect(labels(choice(workflow, 'action-0-kind'))).toEqual([
      'Send an email',
      'Move to a stage',
      'Add a tag',
      'Ask a teammate to review',
      'Tell the team',
      'Create a task',
      'Call a webhook',
      'Offer an interview',
      'Ask the team to review before turning them down',
    ]);
    const withFollowUp = flow(workflow.automations, { followUpQuestionnaires: [{ id: 'more', name: 'More details', questions: [] }] });
    expect(labels(choice(withFollowUp, 'action-0-kind'))).toContain('Send a questionnaire');
  });

  it('keeps a saved rejection readable, and offers it nowhere else', () => {
    const knockout = flow([
      { ...automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'Reject', reason: 'Not eligible' }], [{ fact: 'answers.right_to_work', operator: 'EqualTo', value: 'no' }]), allowAutoReject: true },
    ]);

    expect(readSentence(sentence(knockout))).toContain('turn them down, because Not eligible');
    expect(labels(choice(knockout, 'action-0-kind'))).toContain('Turn them down');
    expect(labels(choice(knockout, 'add-action'))).not.toContain('Turn them down');
  });

  it('starts a step from its kind and gives each kind its own blank', () => {
    const email = choice(workflow, 'action-0-kind').apply('SendEmail');
    expect(email.actions).toEqual([{ type: 'SendEmail', emailTemplate: 'application-received' }]);
    expect(labels(choice(flow([email]), 'action-0-arg'))).toEqual(['Application received', 'Moved to the next step']);
    expect(choice(flow([email]), 'action-0-arg').apply('stage-change').actions[0]).toEqual({ type: 'SendEmail', emailTemplate: 'stage-change' });
  });

  it('asks the team or a teammate to review', () => {
    const review = flow([automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AssignReviewer' }])]);

    expect(labels(choice(review, 'action-0-arg'))).toEqual(['The team', 'Dana Whitfield']);
    expect(choice(review, 'action-0-arg').apply('u-1').actions[0]).toEqual({ type: 'AssignReviewer', assigneeUserId: 'u-1' });
    expect(choice(review, 'action-0-arg').apply('').actions[0]).toEqual({ type: 'AssignReviewer' });
    const gone = flow([automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AssignReviewer', assigneeUserId: 'u-9' }])]);
    expect(readSentence(sentence(gone))).toContain('ask a former teammate to review');
  });

  it('types the text a step needs and shows what is still blank', () => {
    const blankTag = flow([automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AddTag', tag: '' }])]);

    expect(blankOf(blankTag, 'action-0-arg')).toMatchObject({ kind: 'text', value: '', placeholder: 'name the tag' });
    expect((blankOf(blankTag, 'action-0-arg') as { apply: (value: string) => Automation }).apply('Referral').actions[0]).toEqual({ type: 'AddTag', tag: 'Referral' });
    expect(blankOf(blankTag, 'action-0-arg').name).toBe('Action 1: tag name');
  });

  it('offers every stage to move to except one that turns someone down', () => {
    const move = flow([automation('a', { type: 'StageEntered', stageId: 'screening' }, [{ type: 'MoveToStage', stageId: 'trial' }])]);

    expect(labels(choice(move, 'action-0-arg'))).toEqual(['Applied', 'Phone call', 'Trial shift', 'Interview', 'Offer', 'Hired']);
  });

  it('adds a step at the end and removes one from its own list', () => {
    const next = choice(workflow, 'add-action').apply('NotifyTeam');
    expect(next.actions).toEqual([{ type: 'AddTag', tag: 'x' }, { type: 'NotifyTeam', note: '' }]);
    expect(choice(flow([next]), 'action-0-kind').remove?.apply().actions).toEqual([{ type: 'NotifyTeam', note: '' }]);
  });

  it('keeps a time limit to reminders', () => {
    const limited = flow([automation('a', { type: 'SlaBreached', stageId: 'screening' }, [{ type: 'NotifyTeam', note: 'Late' }])]);

    expect(labels(choice(limited, 'action-0-kind'))).toEqual(['Add a tag', 'Ask a teammate to review', 'Tell the team', 'Create a task']);
  });
});

describe('after a check', () => {
  const checking = (actions: AutomationAction[], extra: Automation[] = [], rules: AutomationCondition[] = [checkRule]) =>
    flow([automation('check', { type: 'ApplicationSubmitted' }, actions, rules), ...extra]);

  it('offers only asking a teammate to review, adding a tag and moving to a safe stage', () => {
    const workflow = checking([{ type: 'AddTag', tag: 'Look' }]);

    expect(labels(choice(workflow, 'add-action'))).toEqual(['Move to a stage', 'Add a tag', 'Ask a teammate to review']);
    expect(labels(choice(workflow, 'action-0-kind'))).toEqual(['Move to a stage', 'Add a tag', 'Ask a teammate to review']);
  });

  it('says why in one line, in the list of steps', () => {
    const workflow = checking([{ type: 'AddTag', tag: 'Look' }]);

    expect(choice(workflow, 'add-action').note).toBe(checkGuardrailLine);
    expect(checkGuardrailLine).toBe('A check can only ask for a closer look. A person decides.');
    expect(choice(flow([automation('a', { type: 'ApplicationSubmitted' }, [mail])]), 'add-action').note).toBeUndefined();
  });

  it('is the same when the trigger is the checks finishing, with no condition', () => {
    const workflow = flow([automation('check', { type: 'VerificationAssessed' }, [{ type: 'AddTag', tag: 'x' }])]);

    expect(labels(choice(workflow, 'add-action'))).toEqual(['Move to a stage', 'Add a tag', 'Ask a teammate to review']);
  });

  it('moves only into a review stage, never one that books or turns anyone down', () => {
    const workflow = checking([{ type: 'MoveToStage', stageId: 'screening' }]);

    expect(labels(choice(workflow, 'action-0-arg'))).toEqual(['Phone call', 'Trial shift', 'Interview']);
  });

  it('leaves out a review stage whose own automations a check could not start', () => {
    const books = automation('books', { type: 'StageEntered', stageId: 'screening' }, [mail]);
    const workflow = checking([{ type: 'MoveToStage', stageId: 'trial' }], [books]);

    const targets = choice(workflow, 'action-0-arg');
    expect(labels(targets)).toEqual(['Trial shift', 'Interview']);
    expect(targets.note).toBe('Stages a check moves people into are left out: what runs there can only ask for a closer look.');
  });

  it('keeps a stage whose own automations only ask for a closer look', () => {
    const tags = automation('tags', { type: 'StageEntered', stageId: 'screening' }, [{ type: 'AddTag', tag: 'Seen' }]);
    const workflow = checking([{ type: 'MoveToStage', stageId: 'trial' }], [tags]);

    expect(labels(choice(workflow, 'action-0-arg'))).toEqual(['Phone call', 'Trial shift', 'Interview']);
  });

  it('does not offer a check while the automation has a step a check cannot start', () => {
    const emailing = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail])]);

    expect(labels(choice(emailing, 'add-condition'))).not.toContain('Verification');
    expect(choice(emailing, 'add-condition').note).toBe(leftOutLine);
    expect(ids(choice(emailing, 'trigger'))).not.toContain('VerificationAssessed');
    expect(choice(emailing, 'trigger').note).toBe(leftOutLine);
    const tagging = flow([automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AddTag', tag: 'x' }])]);
    expect(labels(choice(tagging, 'add-condition'))).toContain('Verification');
    expect(ids(choice(tagging, 'trigger'))).toContain('VerificationAssessed');
    expect(choice(tagging, 'add-condition').note).toBeUndefined();
  });

  it('does not offer a stage trigger on a stage a check moves people into, when the automation books or emails', () => {
    const route = automation('route', { type: 'VerificationAssessed' }, [{ type: 'MoveToStage', stageId: 'screening' }]);
    const workflow = flow([route, automation('mine', { type: 'StageEntered', stageId: 'trial' }, [mail])]);

    const stagesOffered = choice(workflow, 'trigger-stage', 1);
    expect(labels(stagesOffered)).not.toContain('Phone call');
    expect(labels(stagesOffered)).toContain('Trial shift');
    expect(stagesOffered.note).toBe('Stages a check moves people into are left out: what runs there can only ask for a closer look.');
  });

  it('does not offer a tag when another automation reads tags in a way a check\'s tag would tangle', () => {
    const readsAbsence = automation('reads', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'application.tags', operator: 'Contains', value: 'referral', negate: true }]);
    const readsOne = automation('reads', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'application.tags', operator: 'Contains', value: 'referral' }]);
    const check = automation('check', { type: 'VerificationAssessed' }, [{ type: 'AssignReviewer' }]);

    expect(labels(choice(flow([check, readsAbsence]), 'add-action'))).toEqual(['Move to a stage', 'Ask a teammate to review']);
    expect(labels(choice(flow([check, readsOne]), 'add-action'))).toEqual(['Move to a stage', 'Add a tag', 'Ask a teammate to review']);
  });

  it('does not offer a time-limit reminder anything but the reminders', () => {
    const limited = flow([automation('a', { type: 'SlaBreached', stageId: 'screening' }, [{ type: 'NotifyTeam', note: 'Late' }])]);

    expect(labels(choice(limited, 'add-action'))).toEqual(['Add a tag', 'Ask a teammate to review', 'Tell the team', 'Create a task']);
  });

  it('every choice the sentence offers leaves the workflow with no new refused step', () => {
    const workflows = [
      checking([{ type: 'AddTag', tag: 'Look' }]),
      flow([automation('route', { type: 'VerificationAssessed' }, [{ type: 'MoveToStage', stageId: 'screening' }]), automation('hop', { type: 'StageEntered', stageId: 'screening' }, [{ type: 'AddTag', tag: 'x' }])]),
      flow([
        automation('check', { type: 'VerificationAssessed' }, [{ type: 'AddTag', tag: 'Check' }]),
        automation('reads', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'application.tags', operator: 'Contains', value: 'zzz' }]),
      ]),
    ];

    for (const workflow of workflows) {
      workflow.automations.forEach((_, index) => {
        const before = violations(workflow);
        const offered = blanksOf(sentence(workflow, index)).filter((blank): blank is ChoiceBlank => blank.kind === 'choice' && (blank.adds === true || blank.id.endsWith('-kind') || blank.id === 'trigger'));
        for (const blank of offered) {
          for (const entry of blank.choices) {
            const next = blank.apply(entry.id);
            const after = violations({ ...workflow, automations: workflow.automations.map((existing, i) => (i === index ? next : existing)) });
            expect([...after].filter((key) => !before.has(key)), `${blank.id} -> ${entry.label}`).toEqual([]);
          }
        }
      });
    }
  });
});

describe('the guardrail, ported from the validator', () => {
  const key = (workflow: HiringWorkflow) => [...violations(workflow)].toSorted();

  it('cannot be laundered through a tag', () => {
    const workflow = flow([
      automation('tagger', { type: 'VerificationAssessed' }, [{ type: 'AddTag', tag: 'Verification-Review' }], [checkRule]),
      automation('mailer', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'application.tags', operator: 'Contains', value: 'verification-review' }]),
      automation('unrelated', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'application.tags', operator: 'Contains', value: 'referral' }]),
    ]);

    expect(key(workflow)).toEqual(['mailer#0']);
  });

  it('cannot be laundered through a stage, even along a chain', () => {
    const workflow = flow([
      automation('route', { type: 'VerificationAssessed' }, [{ type: 'MoveToStage', stageId: 'screening' }]),
      automation('hop', { type: 'StageEntered', stageId: 'screening' }, [{ type: 'MoveToStage', stageId: 'interview' }]),
      automation('queue', { type: 'TimeElapsed', stageId: 'interview', afterHours: 72 }, [{ type: 'QueueForRejectionReview', reason: 'Stalled' }]),
    ]);

    expect(key(workflow)).toEqual(['queue#0']);
  });

  it('cannot be laundered through the stage it moves an application out of', () => {
    const custom = [...stages.slice(0, 2), { id: 'parked', name: 'Parked', kind: 'Custom' as const }, ...stages.slice(2)];
    const workflow = flow(
      [
        automation('route', { type: 'VerificationAssessed' }, [{ type: 'MoveToStage', stageId: 'screening' }]),
        automation('park', { type: 'StageExited', stageId: 'screening' }, [{ type: 'MoveToStage', stageId: 'parked' }]),
      ],
      { stages: custom },
    );

    expect(key(workflow)).toEqual(['park#0']);
    const ordinary = flow([automation('tag', { type: 'VerificationAssessed' }, [{ type: 'AddTag', tag: 'x' }]), workflow.automations[1]!], { stages: custom });
    expect(key(ordinary)).toEqual([]);
  });

  it('refuses a rejection or an email after a check, but not a tag, a review or a move to a review stage', () => {
    const allowed = flow([automation('a', { type: 'VerificationAssessed' }, [{ type: 'AddTag', tag: 't' }, { type: 'AssignReviewer' }, { type: 'MoveToStage', stageId: 'screening' }], [checkRule])]);
    const refused = flow([
      automation('a', { type: 'VerificationAssessed' }, [mail, { type: 'Reject', reason: 'No' }, { type: 'MoveToStage', stageId: 'rejected' }, { type: 'MoveToStage', stageId: 'offer' }, { type: 'ScheduleInterview' }], [checkRule]),
    ]);

    expect(key(allowed)).toEqual([]);
    expect(key(refused)).toEqual(['a#0', 'a#1', 'a#2', 'a#3', 'a#4']);
  });

  it('reaches a negated tag rule after a time limit, and the stages its readers move into', () => {
    const tagger = automation('late', { type: 'SlaBreached', stageId: 'screening' }, [{ type: 'AddTag', tag: 'overdue' }]);
    const negated = automation('no-referral', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'application.tags', operator: 'Contains', value: 'referral', negate: true }]);

    expect(key(flow([tagger, negated]))).toEqual(['no-referral#0']);
    expect(key(flow([negated]))).toEqual([]);

    const advance = automation('advance', { type: 'ApplicationSubmitted' }, [{ type: 'MoveToStage', stageId: 'interview' }], [{ fact: 'application.tags', operator: 'Contains', value: 'overdue' }]);
    const onInterview = automation('on-interview', { type: 'StageEntered', stageId: 'interview' }, [mail]);
    const onExit = automation('on-exit', { type: 'StageExited', stageId: 'applied' }, [mail]);
    const onDecision = automation('on-decision', { type: 'ReviewerDecision' }, [{ type: 'QueueForRejectionReview', reason: 'No' }]);

    expect(key(flow([tagger, advance, onInterview, onExit, onDecision]))).toEqual(['on-decision#0', 'on-exit#0', 'on-interview#0']);
  });

  it('follows the verification guardrail on a stage a check routes into, for a passed time limit', () => {
    const route = automation('route', { type: 'VerificationAssessed' }, [{ type: 'MoveToStage', stageId: 'screening' }]);

    expect(key(flow([route, automation('remind', { type: 'SlaBreached', stageId: 'screening' }, [{ type: 'NotifyTeam', note: 'Late' }])]))).toEqual(['remind#0']);
    expect(key(flow([route, automation('remind', { type: 'SlaBreached', stageId: 'screening' }, [{ type: 'AssignReviewer' }])]))).toEqual([]);
  });

  it('only a time limit may send a time-limit reminder anything but its four reminders', () => {
    expect(key(flow([automation('limit', { type: 'SlaBreached', stageId: 'screening' }, [mail])]))).toEqual(['limit#0']);
  });

  it('finds the automations a check drives', () => {
    const workflow = flow([
      automation('a', { type: 'VerificationAssessed' }, [{ type: 'AddTag', tag: 'x' }]),
      automation('b', { type: 'ApplicationSubmitted' }, [mail], [{ fact: 'application.tags', operator: 'Contains', value: 'x' }]),
      automation('c', { type: 'ApplicationSubmitted' }, [mail]),
    ]);

    expect([...checkDrivenIds(workflow)].toSorted()).toEqual([0, 1]);
    expect(breaks(workflow, 2, { ...workflow.automations[2]!, trigger: { type: 'VerificationAssessed' } })).toBe(true);
    expect(breaks(workflow, 2, { ...workflow.automations[2]!, name: 'renamed' })).toBe(false);
  });

  it('reads tags as the evaluator does', () => {
    expect(ruleHoldsForTags({ fact: 'application.tags', operator: 'Contains', value: 'REFER' }, ['referral'])).toBe(true);
    expect(ruleHoldsForTags({ fact: 'application.tags', operator: 'EqualTo', value: 'Referral' }, ['referral'])).toBe(true);
    expect(ruleHoldsForTags({ fact: 'application.tags', operator: 'In', values: ['a', 'referral'] }, ['referral'])).toBe(true);
    expect(ruleHoldsForTags({ fact: 'application.tags', operator: 'Exists' }, [])).toBe(false);
    expect(ruleHoldsForTags({ fact: 'application.tags', operator: 'Exists', negate: true }, [])).toBe(true);
    expect(ruleHoldsForTags({ fact: 'application.tags', operator: 'GreaterThan', value: '2' }, ['5'])).toBe(true);
  });
});

describe('thenChoices', () => {
  it('lists the kinds for a new step at the end', () => {
    const workflow = flow([automation('a', { type: 'ApplicationSubmitted' }, [mail])]);

    expect(thenChoices(workflow, 0, null).map((entry) => entry.id)).toEqual([
      'SendEmail',
      'MoveToStage',
      'AddTag',
      'AssignReviewer',
      'NotifyTeam',
      'CreateTask',
      'CallWebhook',
      'ScheduleInterview',
      'QueueForRejectionReview',
    ]);
  });
});

describe('review fixes', () => {
  it('does not offer a passed time limit while a condition reads the hours in the stage', () => {
    const waits = flow([
      automation('a', { type: 'StageEntered', stageId: 'screening' }, [{ type: 'NotifyTeam', note: 'Late' }], [
        { fact: 'application.hoursInStage', operator: 'GreaterThan', value: '24' },
      ]),
    ]);

    expect(ids(choice(waits, 'trigger'))).not.toContain('SlaBreached');
  });

  it("caps a typed tag and note at the validator's limits", () => {
    const tagged = flow([automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AddTag', tag: 'x' }, { type: 'NotifyTeam', note: 'hi' }])]);

    expect((blankOf(tagged, 'action-0-arg') as TextBlank).maxLength).toBe(50);
    expect((blankOf(tagged, 'action-1-arg') as TextBlank).maxLength).toBe(1000);
  });

  it("reads an automation in one line for the list, in the map's words", () => {
    const wf = flow([
      automation('a', { type: 'ApplicationSubmitted' }, [{ type: 'AddTag', tag: 'Not eligible' }], [{ fact: 'verification.authenticity', operator: 'In', values: ['ReviewSuggested', 'VerificationRecommended'] }]),
      automation('b', { type: 'StageEntered', stageId: 'screening' }, [mail]),
      automation('c', { type: 'ApplicationSubmitted' }, [{ type: 'AssignReviewer' }], [checkRule, { fact: 'application.tags', operator: 'Exists' }], 'Any'),
    ]);
    const names = new Map([['stage-change', 'Moved to the next step']]);

    expect(automationLine(wf.automations[0]!, wf)).toBe('When someone applies and Verification is Review or Verify, add the tag "Not eligible".');
    expect(automationLine(wf.automations[1]!, wf, undefined, names)).toBe('When someone moves to Phone call, send the "Moved to the next step" email.');
    expect(automationLine(wf.automations[2]!, wf)).toMatch(/^When someone applies and one of .+ or .+, ask the team to review\.$/);
  });

  it('gives every trigger a subject after "When"', () => {
    const wf = flow([]);
    for (const type of triggerTypes) {
      const trigger: Automation['trigger'] = { type, stageId: triggerNeedsStage(type) || type === 'SlaBreached' ? 'screening' : undefined, afterHours: type === 'TimeElapsed' ? 48 : undefined };
      const line = automationLine({ id: 'a', name: 'a', trigger, conditions: null, actions: [{ type: 'AddTag', tag: 'x' }] }, wf);
      expect(line, type).toMatch(/^When (someone|the|a|an|it|\d) /);
    }
  });
});
