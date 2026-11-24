import type { HiringWorkflow, Question } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, roleCatalogue } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import WorkflowHarness from '../../../test-support/workflow-harness.svelte';
import QuestionSettings from './question-settings.svelte';

const line = () => document.querySelector('[data-must-have-line]')?.textContent?.replace(/\s+/g, ' ').trim();

describe('question settings', () => {
  it("names a file question's types in two words, with how to write them in the hint (W4)", async () => {
    const question = { id: 'sample', type: 'File' as const, label: 'A work sample', acceptedFileTypes: ['.pdf'] };
    render(QuestionSettings, { props: { question } });

    const types = screen.getByRole('textbox', { name: 'File types' });
    expect(types).toHaveAccessibleDescription('Separated by commas, such as .pdf, .docx.');
    expect(types).toHaveValue('.pdf');

    await fireEvent.change(types, { target: { value: '.PDF, .docx,' } });
    expect(question.acceptedFileTypes).toEqual(['.pdf', '.docx']);
  });

  it.each([
    ['ShortText', '300'],
    ['LongText', '5,000'],
  ] as const)('shows the real limit of %s as the empty maximum length', async (type, limit) => {
    const question: Question = { id: 'about', type, label: 'About you' };
    render(QuestionSettings, { props: { question } });

    const length = screen.getByRole('spinbutton', { name: 'Maximum length' });
    expect(length).toHaveAccessibleDescription('characters');
    expect(length).toHaveAttribute('placeholder', limit);
    expect(length).toHaveAttribute('max', limit.replace(',', ''));

    await fireEvent.change(length, { target: { value: '120' } });
    expect(question.maxLength).toBe(120);
    await fireEvent.change(length, { target: { value: '' } });
    expect(question.maxLength).toBeNull();
  });

  it('has no maximum length for a link, and lets go of one it was saved with', async () => {
    const { container, unmount } = render(QuestionSettings, { props: { question: { id: 'site', type: 'Url', label: 'Your site' } as Question } });
    expect(container.querySelector('div')).toBeNull();
    unmount();

    const question: Question = { id: 'site', type: 'Url', label: 'Your site', maxLength: 200 };
    render(QuestionSettings, { props: { question } });
    expect(screen.queryByRole('spinbutton')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Remove maximum length' }));
    expect(question.maxLength).toBeNull();
  });

  it('says what a must-have may be in its Info button, and offers no reason and no rejection', async () => {
    const question: Question = { id: 'licence', type: 'YesNo', label: 'Do you hold a licence?', knockout: { disqualifyingAnswer: false, reason: 'An old note' } };
    const { container } = render(QuestionSettings, { props: { question } });

    await fireEvent.click(screen.getByRole('button', { name: 'About must-have answers' }));
    expect(await screen.findByText(/objective requirement only, such as the right to work or a licence\. Never AI use or an accommodation\./)).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).toBeNull();
    expect(container.textContent).not.toMatch(/reject|An old note/i);
  });

  it('says a must-have does nothing yet, and where to add a step, until an automation reads its answer (A11)', () => {
    const question: Question = { id: 'licence', type: 'YesNo', label: 'Do you hold a licence?', knockout: { disqualifyingAnswer: false, reason: null } };
    // The page's own address for the tab, so a version being edited stays the one shown.
    const automationsHref = '/templates/t-1?version=2&tab=automations';
    render(QuestionSettings, { props: { question, mustHave: { kind: 'nothing' }, automationsHref } });

    expect(line()).toContain('Nothing happens to this answer yet. Add a step in Automations.');
    expect(screen.getByRole('link', { name: 'Add a step in Automations' })).toHaveAttribute('href', automationsHref);
  });

  it('says nothing about automations where there are none to add to (a library form), or for a question that is not a must-have', () => {
    const question: Question = { id: 'licence', type: 'YesNo', label: 'Do you hold a licence?', knockout: { disqualifyingAnswer: true, reason: null } };
    const { unmount } = render(QuestionSettings, { props: { question } });
    expect(line()).toMatch(/^Candidates who answer .* do not meet this must-have\.$/);
    expect(screen.getByRole('combobox')).toHaveValue('yes');
    expect(screen.queryByRole('link')).toBeNull();
    unmount();

    render(QuestionSettings, { props: { question: { ...question, knockout: null }, mustHave: { kind: 'nothing' } } });
    expect(line()).toBeUndefined();
  });

  it('draws nothing for a choice question, so the open question keeps even gaps', () => {
    const question = { id: 'years', type: 'SingleChoice' as const, label: 'Years in support', options: [] };
    const { container } = render(QuestionSettings, { props: { question } });

    expect(container.querySelector('div')).toBeNull();
  });
});

// The switch in the builder it lives in: the automation it writes is the workflow's own, so what matters is the draft.
describe('a must-have in the form builder', () => {
  const base: HiringWorkflow = {
    stages: [
      { id: 'applied', name: 'Applied', kind: 'Applied' },
      { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
    ],
    questionnaire: { questions: [{ id: 'licence', type: 'YesNo', label: 'Do you hold a licence?', required: true }] },
    automations: [],
  };
  const draft = () => JSON.parse(screen.getByTestId('draft').textContent!) as HiringWorkflow;

  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/organization': () => Response.json({ questionsPerPage: 4 }),
          'GET /v1/form-templates': () => Response.json({ forms: [] }),
          'GET /v1/email-templates': () => Response.json({ templates: [], sendingEnabled: false }),
          'GET /v1/organization/members': () => Response.json([]),
          'GET /v1/organization/roles': () => Response.json(roleCatalogue),
        }),
      ),
    );
    Element.prototype.scrollIntoView ??= () => {};
    Element.prototype.getAnimations = () => [];
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    delete (Element.prototype as Partial<Element>).getAnimations;
  });

  async function openQuestion(workflow: HiringWorkflow = base) {
    const view = render(WithQueryClient, { props: { component: WorkflowHarness, props: { workflow: structuredClone(workflow), mode: 'editor', tab: 'forms' } } });
    await fireEvent.click(await screen.findByRole('button', { name: /^Edit question 1:/ }));
    return view;
  }

  it('sends whoever fails it to Rejection review, follows the answer and the words, and takes it back', async () => {
    const { container } = await openQuestion();
    expect(draft().automations).toEqual([]);

    await fireEvent.click(screen.getByRole('switch', { name: 'Must-have answer' }));
    expect(draft().automations).toHaveLength(1);
    expect(draft().automations[0]).toMatchObject({ actions: [{ type: 'QueueForRejectionReview' }], allowAutoReject: false });
    expect(line()).toMatch(/^Candidates who answer No ?Yes go to Rejection review, where a person decides\. Change what happens$/);
    await expectNoAxeViolations(container);

    await fireEvent.change(screen.getByRole('combobox', { name: 'Answer that fails the must-have' }), { target: { value: 'yes' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'Question 1' }), { target: { value: 'Are you under 18?' } });
    expect(draft().automations).toHaveLength(1);
    expect(draft().automations[0]).toMatchObject({
      name: 'Review anyone who fails "Are you under 18?"',
      conditions: { rules: [{ fact: 'answers.licence', operator: 'EqualTo', value: 'yes' }] },
    });

    await fireEvent.click(screen.getByRole('switch', { name: 'Must-have answer' }));
    expect(draft().automations).toEqual([]);
  });

  it('keeps the question open when Escape closes the Info button', async () => {
    await openQuestion();
    const info = screen.getByRole('button', { name: 'About must-have answers' });
    await fireEvent.click(info);
    expect(await screen.findByText(/objective requirement only/)).toBeInTheDocument();

    await fireEvent.keyDown(info, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByText(/objective requirement only/)).toBeNull());
    expect(screen.getByRole('switch', { name: 'Must-have answer' })).toBeInTheDocument();

    await fireEvent.keyDown(info, { key: 'Escape' });
    expect(await screen.findByRole('button', { name: /^Edit question 1:/ })).toBeInTheDocument();
  });

  it('opens that automation on the Automations tab from Change what happens', async () => {
    await openQuestion();
    await fireEvent.click(screen.getByRole('switch', { name: 'Must-have answer' }));

    const change = screen.getByRole('link', { name: 'Change what happens' });
    expect(change).toHaveAttribute('href', `?tab=automations&show=${draft().automations[0]!.id}`);
    await fireEvent.click(change);

    expect(await screen.findByRole('dialog', { name: 'Review anyone who fails "Do you hold a licence?"' })).toBeInTheDocument();
  });

  it('never rewrites or removes an automation a person edited: the line names it and links to it', async () => {
    const edited = {
      id: 'recipe-knockout-licence',
      name: 'Licence check',
      trigger: { type: 'ApplicationSubmitted' as const },
      conditions: { match: 'All' as const, rules: [{ fact: 'answers.licence', operator: 'EqualTo' as const, value: 'no' }] },
      actions: [{ type: 'QueueForRejectionReview' as const, reason: 'No licence' }],
      enabled: true,
      allowAutoReject: false,
    };
    await openQuestion({
      ...base,
      questionnaire: { questions: [{ ...base.questionnaire.questions[0]!, knockout: { disqualifyingAnswer: false, reason: null } }] },
      automations: [edited],
    });

    const settings = document.querySelector<HTMLElement>('[data-must-have-line]')!;
    expect(within(settings).getByRole('link', { name: 'Licence check' })).toHaveAttribute('href', '?tab=automations&show=recipe-knockout-licence');

    await fireEvent.click(screen.getByRole('switch', { name: 'Must-have answer' }));
    expect(draft().automations).toEqual([edited]);
    expect(line()).toBe('The automation Licence check still reads this answer.');
  });

  it('is removed with its question', async () => {
    await openQuestion();
    await fireEvent.click(screen.getByRole('switch', { name: 'Must-have answer' }));
    await fireEvent.click(screen.getByRole('button', { name: /^Actions for Do you hold a licence/ }));
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }));

    expect(draft().questionnaire.questions).toEqual([]);
    expect(draft().automations).toEqual([]);
  });
});
