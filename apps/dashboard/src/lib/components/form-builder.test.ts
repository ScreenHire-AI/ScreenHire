import type { Question } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import FormBuilderHarness from '../../test-support/form-builder-harness.svelte';

// jsdom has no Web Animations, and a row leaving the list's `animate:flip` asks for its running ones. Only here: bits-ui
// reads the same method to decide when an overlay has finished closing.
beforeAll(() => {
  Element.prototype.getAnimations = () => [];
});
afterAll(() => {
  delete (Element.prototype as Partial<Element>).getAnimations;
});

const questions: Question[] = [
  { id: 'right_to_work', type: 'YesNo', label: 'Do you have the right to work here?', required: true },
  {
    id: 'years',
    type: 'SingleChoice',
    label: 'How many years have you worked in support?',
    helpText: 'Paid work only.',
    options: [
      { id: 'under_3', label: 'Less than 3' },
      { id: 'over_3', label: '3 or more' },
    ],
  },
  { id: 'notice', type: 'ShortText', label: 'What is your notice period?' },
];

function setup(errors: Record<string, string[]> = {}) {
  let latest: Question[] = questions;
  const view = render(FormBuilderHarness, { props: { initial: questions, errors, onchange: (next: Question[]) => (latest = next) } });
  return { ...view, list: () => latest };
}

const panel = () => screen.getByRole('region', { name: 'Screening questions' });
const edit = (name: RegExp) => within(panel()).getByRole('button', { name });
const openRow = () => panel().querySelector<HTMLElement>('[data-open]');
const press = (element: HTMLElement) => fireEvent.pointerDown(element, { button: 0, pointerType: 'mouse' });

describe('form builder', () => {
  it('reads every question as text, its kind top right and an asterisk when required, with none open', async () => {
    const { container } = setup();

    expect(openRow()).toBeNull();
    const first = edit(/^Edit question 1: /);
    expect(first).toHaveAccessibleName('Edit question 1: Do you have the right to work here?, required Yes or no');
    expect(first).toHaveAttribute('aria-expanded', 'false');
    expect(first).toHaveTextContent('Do you have the right to work here?*, required');
    expect(first).toHaveTextContent('Yes or no');
    // A choice question lists its options with the mark the candidate sees, and its help line.
    const second = edit(/^Edit question 2: /);
    expect(second).toHaveTextContent('Paid work only.');
    expect(second).toHaveTextContent('Less than 3');
    expect(second.querySelectorAll('.rounded-full')).toHaveLength(2);
    expect(edit(/^Edit question 3: /)).not.toHaveTextContent('*');
    await expectNoAxeViolations(container);
  });

  it('opens one question at a time, with the accent bar, and focuses its text', async () => {
    const { container } = setup();

    await fireEvent.click(edit(/^Edit question 3: /));

    const row = openRow()!;
    expect(row.querySelector('.bg-primary')).not.toBeNull();
    const text = within(row).getByRole('textbox', { name: 'Question 3' });
    await waitFor(() => expect(text).toHaveFocus());
    expect(text).toHaveValue('What is your notice period?');
    expect(within(row).getByRole('button', { name: 'Answer: Short text' })).toBeInTheDocument();
    expect(within(row).getByRole('switch', { name: 'Required' })).toHaveAttribute('aria-checked', 'false');
    await expectNoAxeViolations(container);

    await fireEvent.click(edit(/^Edit question 1: /));
    expect(panel().querySelectorAll('[data-open]')).toHaveLength(1);
    expect(within(openRow()!).getByRole('textbox', { name: 'Question 1' })).toBeInTheDocument();
  });

  it('keeps the focus in the text while typing, a space included, and saves each edit', async () => {
    const { list } = setup();
    await fireEvent.click(edit(/^Edit question 3: /));
    const text = within(openRow()!).getByRole('textbox', { name: 'Question 3' });
    await waitFor(() => expect(text).toHaveFocus());

    await fireEvent.keyDown(text, { key: ' ' });
    await fireEvent.input(text, { target: { value: 'When could you start?' } });

    expect(text).toHaveFocus();
    expect(screen.queryByRole('menu')).toBeNull();
    expect(list()[2]!.label).toBe('When could you start?');
  });

  it('keeps the text and help line to one line each: Enter adds no break, and a pasted one becomes a space', async () => {
    const { list } = setup();
    await fireEvent.click(edit(/^Edit question 2: /));
    const row = openRow()!;
    const text = within(row).getByRole('textbox', { name: 'Question 2' });

    expect(await fireEvent.keyDown(text, { key: 'Enter' })).toBe(false);
    await fireEvent.input(text, { target: { value: 'How many years\nin support?' } });
    await fireEvent.input(within(row).getByRole('textbox', { name: 'Help text' }), { target: { value: 'Paid work\r\n only.' } });

    expect(list()[1]).toMatchObject({ label: 'How many years in support?', helpText: 'Paid work only.' });
  });

  it('closes the open question on Escape and gives its row the focus back', async () => {
    setup();
    await fireEvent.click(edit(/^Edit question 3: /));
    const text = within(openRow()!).getByRole('textbox', { name: 'Question 3' });
    await waitFor(() => expect(text).toHaveFocus());

    await fireEvent.keyDown(text, { key: 'Escape' });

    expect(openRow()).toBeNull();
    await waitFor(() => expect(edit(/^Edit question 3: /)).toHaveFocus());
  });

  it('changes the answer kind from the answer menu, ticking the current one and keeping the text', async () => {
    const { list } = setup();
    await fireEvent.click(edit(/^Edit question 3: /));

    await press(within(openRow()!).getByRole('button', { name: 'Answer: Short text' }));

    const menu = await screen.findByRole('menu');
    expect(within(menu).getByText('General')).toBeInTheDocument();
    expect(within(menu).getByText('More')).toBeInTheDocument();
    expect(within(menu).getAllByRole('menuitemradio').map((item) => item.textContent?.trim())).toEqual([
      'Yes or no',
      'Choice',
      'Multi-choice',
      'Short text',
      'Long text',
      'Number',
      'Link',
      'File',
    ]);
    expect(within(menu).getByRole('menuitemradio', { name: 'Short text' })).toHaveAttribute('aria-checked', 'true');

    await fireEvent.click(within(menu).getByRole('menuitemradio', { name: 'Multi-choice' }));

    expect(list()[2]).toMatchObject({ type: 'MultiChoice', label: 'What is your notice period?' });
    // A new choice question starts with options, each with a square: pick any.
    const row = openRow()!;
    expect(within(row).getByRole('list', { name: /^Options for / })).toBeInTheDocument();
    expect(row.querySelectorAll('.rounded-sm').length).toBeGreaterThan(0);
  });

  it('edits options with the mark the candidate sees, ending in Add option', async () => {
    const { list } = setup();
    await fireEvent.click(edit(/^Edit question 2: /));
    const row = openRow()!;

    await fireEvent.click(within(row).getByRole('button', { name: 'Add option' }));
    expect(list()[1]!.options!.map((option) => option.label)).toEqual(['Less than 3', '3 or more', 'Option 3']);

    await fireEvent.input(within(row).getByRole('textbox', { name: 'Option 3' }), { target: { value: '10 or more' } });
    await fireEvent.click(within(row).getByRole('button', { name: 'Remove option Less than 3' }));
    expect(list()[1]!.options!.map((option) => option.label)).toEqual(['3 or more', '10 or more']);

    // Options reorder with their own grip (A7).
    await fireEvent.click(within(row).getByRole('button', { name: 'Reorder 10 or more' }));
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Move up' }));
    expect(list()[1]!.options!.map((option) => option.label)).toEqual(['10 or more', '3 or more']);
  });

  it('shows a yes-or-no question its fixed choices', async () => {
    setup();
    await fireEvent.click(edit(/^Edit question 1: /));

    const choices = within(openRow()!).getByRole('list', { name: 'Choices' });
    expect(within(choices).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Yes', 'No']);
  });

  it('duplicates a question straight after the original, with its own id, and opens the copy', async () => {
    const { list } = setup();
    await fireEvent.click(edit(/^Edit question 1: /));

    await fireEvent.click(within(openRow()!).getByRole('button', { name: 'Duplicate Do you have the right to work here?' }));

    expect(list().map((question) => question.id)).toEqual(['right_to_work', 'right_to_work_copy', 'years', 'notice']);
    expect(list()[1]).toMatchObject({ type: 'YesNo', label: 'Do you have the right to work here?', required: true });
    const text = within(openRow()!).getByRole('textbox', { name: 'Question 2' });
    await waitFor(() => expect(text).toHaveFocus());
  });

  it('switches Required on and off', async () => {
    const { list } = setup();
    await fireEvent.click(edit(/^Edit question 3: /));

    await fireEvent.click(within(openRow()!).getByRole('switch', { name: 'Required' }));

    expect(list()[2]!.required).toBe(true);
  });

  it('deletes a question with no confirmation and moves the focus to the next one', async () => {
    const { list } = setup();
    await fireEvent.click(edit(/^Edit question 2: /));

    await fireEvent.click(within(openRow()!).getByRole('button', { name: 'Delete How many years have you worked in support?' }));

    expect(list().map((question) => question.id)).toEqual(['right_to_work', 'notice']);
    await waitFor(() => expect(edit(/^Edit question 2: What is your notice period/)).toHaveFocus());
  });

  it('adds a question of the kind chosen in Add a question, open at the end', async () => {
    const { list } = setup();

    await press(within(panel()).getByRole('button', { name: 'Add a question' }));
    const menu = await screen.findByRole('menu');
    // Nothing is ticked: the new question has no kind yet.
    expect(within(menu).queryAllByRole('menuitemradio')).toHaveLength(0);
    await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Number' }));

    expect(list().at(-1)).toMatchObject({ id: 'question', type: 'Number', label: 'New question' });
    await waitFor(() => expect(within(openRow()!).getByRole('textbox', { name: 'Question 4' })).toHaveFocus());
  });

  it("moves a question with its grip's menu", async () => {
    const { list } = setup();

    await fireEvent.click(screen.getByRole('button', { name: 'Reorder What is your notice period?' }));
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Move to top' }));

    expect(list().map((question) => question.id)).toEqual(['notice', 'right_to_work', 'years']);
  });

  it('shows a problem under its question whether it is open or not (W4)', async () => {
    setup({ notice: ['Give the question its text.'] });

    expect(within(panel()).getByText('Give the question its text.')).toBeInTheDocument();
    expect(edit(/^Edit question 3: /)).toHaveAccessibleDescription('Give the question its text.');

    await fireEvent.click(edit(/^Edit question 3: /));
    const text = within(openRow()!).getByRole('textbox', { name: 'Question 3' });
    expect(text).toHaveAttribute('aria-invalid', 'true');
    expect(text).toHaveAccessibleDescription('Give the question its text.');
  });
});

describe('form builder sections', () => {
  type Section = { id: string; name: string; questionIds: string[] };
  function sectioned(initialSections: Section[] | null = null) {
    let latest: Question[] = questions;
    let sections: Section[] | null = initialSections;
    const view = render(FormBuilderHarness, {
      props: {
        initial: questions,
        withSections: true,
        initialSections,
        onchange: (next: Question[]) => (latest = next),
        onsections: (next: Section[] | null) => (sections = next),
      },
    });
    return { ...view, list: () => latest, sections: () => sections };
  }

  const twoSections: Section[] = [
    { id: 'eligibility', name: 'Eligibility', questionIds: ['right_to_work'] },
    { id: 'experience', name: 'Experience', questionIds: ['years', 'notice'] },
  ];

  it('offers Add a section only to a questionnaire, never to a scorecard', () => {
    setup();
    expect(within(panel()).queryByRole('button', { name: 'Add a section' })).toBeNull();
  });

  it('puts every question in a first section and opens a second, empty one at its name', async () => {
    const { sections, container } = sectioned();

    await fireEvent.click(within(panel()).getByRole('button', { name: 'Add a section' }));

    expect(sections()).toEqual([
      { id: 'section_1', name: 'Section 1', questionIds: ['right_to_work', 'years', 'notice'] },
      { id: 'section_2', name: 'Section 2', questionIds: [] },
    ]);
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Section 2 name' })).toHaveFocus());
    expect(within(panel()).getByText('3 questions')).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('reads each section as a row before its questions, and renames it in place', async () => {
    const { sections } = sectioned(twoSections);

    const rows = [...panel().querySelectorAll('[data-section-row], [data-question-row]')].map(
      (row) => row.getAttribute('data-section-row') ?? row.getAttribute('data-question-row'),
    );
    expect(rows).toEqual(['eligibility', 'right_to_work', 'experience', 'years', 'notice']);

    await fireEvent.input(screen.getByRole('textbox', { name: 'Section 2 name' }), { target: { value: 'Your experience' } });
    expect(sections()?.[1]?.name).toBe('Your experience');
  });

  it('moves a question into the section before it with Move up across the section row', async () => {
    const { sections, list } = sectioned(twoSections);

    await fireEvent.click(screen.getByRole('button', { name: 'Reorder How many years have you worked in support?' }));
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Move up' }));

    expect(sections()?.map((section) => section.questionIds)).toEqual([['right_to_work', 'years'], ['notice']]);
    expect(list().map((question) => question.id)).toEqual(['right_to_work', 'years', 'notice']);
  });

  it('gives a deleted section its questions, and deleting the last goes back to automatic pages', async () => {
    const { sections, list } = sectioned(twoSections);

    await fireEvent.click(within(panel()).getByRole('button', { name: 'Delete section Experience' }));

    expect(sections()).toEqual([{ id: 'eligibility', name: 'Eligibility', questionIds: ['right_to_work', 'years', 'notice'] }]);
    expect(list().map((question) => question.id)).toEqual(['right_to_work', 'years', 'notice']);

    await fireEvent.click(within(panel()).getByRole('button', { name: 'Delete section Eligibility' }));
    expect(sections()).toBeNull();
  });
});
