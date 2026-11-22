// Try it's rules (#lib/try-it.ts): who can apply, the moments of each stage, and what each step the evaluator ran looks
// like on screen. The evaluator itself is the API's; these tests feed it as its answers would come back.
import type { HiringWorkflow, SimulatedAutomation } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  arrivalTriggers,
  candidateFor,
  candidateFromForm,
  emailOf,
  factsOf,
  nextStage,
  ranOf,
  scenariosOf,
  splitThings,
  thingsOf,
  trackerAt,
  waitOf,
  walkStages,
  type Thing,
} from '#lib/try-it.js';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'screening', name: 'Phone call', kind: 'Screening', slaDays: 3 },
    { id: 'trial', name: 'Trial shift', kind: 'Interview' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
    { id: 'rejected', name: 'Not progressing', kind: 'Rejected' },
    { id: 'withdrawn', name: 'Withdrawn', kind: 'Withdrawn' },
  ],
  questionnaire: {
    questions: [
      { id: 'licence', type: 'YesNo', label: 'Do you hold a driving licence?', required: true, knockout: { disqualifyingAnswer: false } },
      { id: 'tools', type: 'ShortText', label: 'Which tools do you own?', required: false },
    ],
  },
  automations: [
    { id: 'trial-wait', name: 'Chase the trial', trigger: { type: 'TimeElapsed', stageId: 'trial', afterHours: 50 }, conditions: null, actions: [{ type: 'NotifyTeam' }] },
  ],
};

const names = new Map([['application-received', 'Application received']]);
const matched = (automationId: string, actions: SimulatedAutomation['actions']): SimulatedAutomation => ({ automationId, name: automationId, outcome: 'Matched', rules: [], actions, reason: null });

describe('who is applying', () => {
  it('offers four cards where the form has a must-have question, and words the one who cannot work here from it', () => {
    expect(scenariosOf(workflow).map((scenario) => scenario.id)).toEqual(['fit', 'cannot', 'check', 'form']);
    expect(scenariosOf(workflow)[1]).toMatchObject({ title: "Can't work here", description: 'Answers No to "Do you hold a driving licence?"' });
  });

  it('ends the sentence once: a full stop only after a question with no punctuation of its own', () => {
    const statement = { ...workflow, questionnaire: { questions: [{ ...workflow.questionnaire!.questions[0]!, label: 'I hold a driving licence' }] } };
    expect(scenariosOf(statement)[1]).toMatchObject({ description: 'Answers No to "I hold a driving licence".' });
  });

  it("leaves out Can't work here when no question is a must-have: it would be the same candidate as a good fit", () => {
    const open = { ...workflow, questionnaire: { questions: [workflow.questionnaire.questions[1]!] } };
    expect(scenariosOf(open).map((scenario) => scenario.id)).toEqual(['fit', 'check', 'form']);
  });

  it('builds each candidate from the form: a good fit passes, the one who cannot work here fails, the flagged one passes but is flagged', () => {
    expect(candidateFor(workflow, 'fit')).toEqual({ answers: { licence: ['yes'] }, flagged: false });
    expect(candidateFor(workflow, 'cannot')).toEqual({ answers: { licence: ['no'] }, flagged: false, saidNoTo: 'Do you hold a driving licence?' });
    expect(candidateFor(workflow, 'check')).toEqual({ answers: { licence: ['yes'] }, flagged: true });
  });

  it('reads the form a person filled in, leaving out blanks, and knows when they said no to a must-have', () => {
    const candidate = candidateFromForm(workflow, { licence: 'no', tools: '  ', other: ['a', 'b'], skipped: undefined });
    expect(candidate).toEqual({ answers: { licence: ['no'], other: ['a', 'b'] }, flagged: false, saidNoTo: 'Do you hold a driving licence?' });
  });

  it('keeps the verification levels inside the facts: calm for most, asking a person to look when flagged', () => {
    expect(factsOf(candidateFor(workflow, 'fit'), ['early'])).toMatchObject({ verificationAuthenticity: 'NoConcerns', verificationAiAssistance: 'NotIndicated', tags: ['early'], hoursInStage: null });
    expect(factsOf(candidateFor(workflow, 'check'), [], 72)).toMatchObject({ verificationAuthenticity: 'VerificationRecommended', verificationAiAssistance: 'StrongIndicators', hoursInStage: 72 });
  });
});

describe('the walk', () => {
  it('walks the stages in order and never into a way out', () => {
    expect(walkStages(workflow).map((stage) => stage.id)).toEqual(['applied', 'screening', 'trial', 'hired']);
    expect(nextStage(workflow, 'trial')?.id).toBe('hired');
    expect(nextStage(workflow, 'hired')).toBeUndefined();
  });

  it('applies, finishes the checks and enters the first stage; any other stage is entered', () => {
    expect(arrivalTriggers(workflow, 'applied')).toEqual([{ type: 'ApplicationSubmitted' }, { type: 'VerificationAssessed' }, { type: 'StageEntered', stageId: 'applied' }]);
    expect(arrivalTriggers(workflow, 'trial')).toEqual([{ type: 'StageEntered', stageId: 'trial' }]);
  });

  it('lights the stage they are in on the rail, with the ones before it passed', () => {
    const tracker = trackerAt(workflow, 'screening');
    expect(tracker.steps.map((step) => step.state)).toEqual(['passed', 'current', 'upcoming', 'upcoming']);
    expect([tracker.position, tracker.total]).toEqual([2, 4]);
  });

  it("lets the time limit's days pass, else the first time-passed automation's in whole days, else nothing", () => {
    expect(waitOf(workflow, workflow.stages[1]!)).toEqual({ days: 3, triggers: [{ type: 'SlaBreached', stageId: 'screening' }], overdue: true });
    expect(waitOf(workflow, workflow.stages[2]!)).toEqual({ days: 3, triggers: [{ type: 'TimeElapsed', stageId: 'trial' }], overdue: false });
    expect(waitOf(workflow, workflow.stages[0]!)).toBeNull();
  });
});

describe('what a stage shows', () => {
  const fit = candidateFor(workflow, 'fit');

  it('draws each email once, by the template the worker sends for the action', () => {
    expect(emailOf({ type: 'SendEmail', emailTemplate: 'application-received' })).toBe('application-received');
    expect(emailOf({ type: 'SendQuestionnaire', questionnaireId: 'q' })).toBe('questionnaire-request');
    expect(emailOf({ type: 'ScheduleInterview' })).toBe('interview-invitation');
    expect(emailOf({ type: 'Reject' })).toBe('application-rejected');
    expect(emailOf({ type: 'AddTag', tag: 'x' })).toBeNull();

    const ran = ranOf([matched('a', [{ type: 'SendEmail', emailTemplate: 'application-received' }]), matched('b', [{ type: 'SendEmail', emailTemplate: 'application-received' }])]);
    expect(thingsOf(workflow, ran, fit, { first: true, templateNames: names }).filter((thing) => thing.kind === 'email')).toHaveLength(1);
  });

  it('shows team work as chips, then says the person decides when they move on', () => {
    const ran = ranOf([matched('a', [{ type: 'NotifyTeam' }, { type: 'AddTag', tag: 'early' }])]);
    expect(thingsOf(workflow, ran, fit, { first: false, templateNames: names })).toEqual([
      { kind: 'task', text: 'Tells the team', action: 'NotifyTeam' },
      { kind: 'task', text: 'Adds the tag "early"', action: 'AddTag' },
      { kind: 'decide', text: 'You decide when Jordan moves on.' },
    ]);
  });

  it('says nothing happens by itself in a stage where nothing ran', () => {
    expect(thingsOf(workflow, [], fit, { first: false, templateNames: names })).toEqual([{ kind: 'decide', text: 'Nothing happens by itself here. You decide when Jordan moves on.' }]);
  });

  it('never says they move on from the last stage, where there is nowhere to go', () => {
    expect(thingsOf(workflow, [], fit, { first: false, templateNames: names, last: true })).toEqual([{ kind: 'decide', text: 'Nothing happens by itself here. This is the last stage of your process.' }]);
    const ran = ranOf([matched('a', [{ type: 'NotifyTeam' }])]);
    expect(thingsOf(workflow, ran, fit, { first: false, templateNames: names, last: true }).at(-1)).toEqual({ kind: 'decide', text: 'This is the last stage of your process.' });
  });

  it('puts someone who said no on the review list in plain words, and turns nobody down', () => {
    const ran = ranOf([matched('a', [{ type: 'QueueForRejectionReview', reason: 'No licence' }])]);
    const [line] = thingsOf(workflow, ran, candidateFor(workflow, 'cannot'), { first: true, templateNames: names });
    expect(line).toEqual({ kind: 'decide', text: 'Jordan said no to "Do you hold a driving licence?" They wait on your review list, and nobody is turned down until you decide.' });
  });

  it('says only that the checks flagged something and a person decides, on the first stage', () => {
    const things = thingsOf(workflow, [], candidateFor(workflow, 'check'), { first: true, templateNames: names });
    expect(things).toEqual([{ kind: 'decide', text: 'The checks flagged something about Jordan. A person looks and decides what happens.' }]);
  });

  it('says when they moved on by itself, when it kept moving them, and when a move or a decline ends the walk', () => {
    expect(thingsOf(workflow, [], fit, { first: false, templateNames: names, movedTo: ['Trial shift'] })[0]).toEqual({ kind: 'moved', text: 'Jordan moved on to Trial shift by itself.' });
    expect(thingsOf(workflow, [], fit, { first: false, templateNames: names, movedTo: ['Hired', 'Phone call'], stopped: true })[0]).toEqual({
      kind: 'moved',
      text: 'Jordan moved on to Hired, then Phone call by itself. It keeps moving them on, so Try it stops here.',
    });
    expect(thingsOf(workflow, [], fit, { first: false, templateNames: names, left: 'Not progressing' }).at(-1)).toEqual({
      kind: 'ended',
      text: 'Jordan moved to Not progressing by itself, which ends the process for them.',
    });
    const declined = thingsOf(workflow, ranOf([matched('a', [{ type: 'Reject', reason: 'No licence' }])]), fit, { first: true, templateNames: names });
    expect(declined.map((thing) => thing.kind)).toEqual(['email', 'ended']);
  });

  it('never words a level, a score or the engine\'s terms', () => {
    const all = [
      ...thingsOf(workflow, ranOf([matched('a', [{ type: 'QueueForRejectionReview' }, { type: 'AssignReviewer' }, { type: 'RequestVerification' }])]), candidateFor(workflow, 'cannot'), { first: true, templateNames: names }),
      ...thingsOf(workflow, [], candidateFor(workflow, 'check'), { first: true, templateNames: names }),
      ...scenariosOf(workflow),
    ];
    const words = JSON.stringify(all);
    for (const forbidden of [/knockout/i, /Verification is/i, /\bVerify\b/, /\bReview\b/, /score/i, /%/, /reject/i]) expect(words).not.toMatch(forbidden);
  });

  it('shows three things at most, keeping the line that says who decides, and puts the rest behind more', () => {
    const things: Thing[] = [
      { kind: 'email', templateId: 'a' },
      { kind: 'email', templateId: 'b' },
      { kind: 'task', text: 'Tells the team', action: 'NotifyTeam' },
      { kind: 'decide', text: 'You decide when Jordan moves on.' },
    ];
    const { shown, hidden } = splitThings(things);
    expect(shown.map((thing) => thing.kind)).toEqual(['email', 'email', 'decide']);
    expect(hidden).toEqual([{ kind: 'task', text: 'Tells the team', action: 'NotifyTeam' }]);
    expect(splitThings(things.slice(2)).hidden).toEqual([]);
  });
});
