// The shared question components (packages/ui) as the dashboard's live preview renders them: the same native controls,
// names and ids the careers apply page serves, here interactive.
import type { Question } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import QuestionnairePreview from './questionnaire-preview.svelte';

const questions: Question[] = [
  { id: 'work_authorization', type: 'YesNo', label: 'Can you work in the UK?', required: true, knockout: { disqualifyingAnswer: false } },
  {
    id: 'shifts',
    type: 'MultiChoice',
    label: 'Which shifts suit you?',
    options: [
      { id: 'days', label: 'Days' },
      { id: 'nights', label: 'Nights' },
    ],
  },
  { id: 'years', type: 'Number', label: 'Years in a warehouse', min: 0, max: 50 },
  { id: 'why', type: 'LongText', label: 'Why this role?', helpText: 'A few sentences.', maxLength: 1000 },
];

describe('QuestionnairePreview', () => {
  it('asks for the candidate details and the resume first, then the questions in order', () => {
    render(QuestionnairePreview, { questions });

    const labels = [...document.querySelectorAll('label[for], legend')].map((element) => element.textContent?.replace(/\s+/g, ' ').trim());
    expect(labels).toEqual([
      'First name',
      'Last name (optional)',
      'Email',
      'Phone (optional)',
      'Resume or CV (optional)',
      'Can you work in the UK?',
      'Which shifts suit you? (optional)',
      'Years in a warehouse (optional)',
      'Why this role? (optional)',
    ]);
  });

  it('renders yes/no as radios and multiple choice as checkboxes, named as the careers form posts them', () => {
    render(QuestionnairePreview, { questions });

    const authorization = screen.getByRole('group', { name: 'Can you work in the UK?' });
    expect(authorization).toHaveAttribute('id', 'field-q-work_authorization');
    expect(within(authorization).getAllByRole('radio').map((radio) => radio.getAttribute('name'))).toEqual(['q:work_authorization', 'q:work_authorization']);
    expect(within(screen.getByRole('group', { name: /Which shifts/ })).getAllByRole('checkbox')).toHaveLength(2);
    expect(screen.getByLabelText(/Resume or CV/)).toHaveAttribute('type', 'file');
  });

  it('gives hints and limits the native way', () => {
    render(QuestionnairePreview, { questions });

    expect(screen.getByText('A number from 0 up to 50.')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /Years in a warehouse/ })).toHaveAttribute('inputmode', 'decimal');
    const why = screen.getByRole('textbox', { name: /Why this role/ });
    expect(why.tagName).toBe('TEXTAREA');
    expect(why).toHaveAttribute('maxlength', '1000');
    expect(why).toHaveAccessibleDescription('A few sentences.');
    expect(screen.getByText('PDF or DOCX, up to 10 MB.')).toBeInTheDocument();
  });

  it('lets the recruiter try answers: one radio at a time, several checkboxes', async () => {
    render(QuestionnairePreview, { questions });

    const yes = screen.getByRole('radio', { name: 'Yes' });
    const no = screen.getByRole('radio', { name: 'No' });
    await fireEvent.click(yes);
    await fireEvent.click(no);
    expect(no).toBeChecked();
    expect(yes).not.toBeChecked();

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Days' }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Nights' }));
    expect(screen.getByRole('checkbox', { name: 'Days' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Nights' })).toBeChecked();
  });

  it('follows the questionnaire as it is edited', async () => {
    const view = render(QuestionnairePreview, { questions });

    await view.rerender({ questions: [...questions, { id: 'site', type: 'Url', label: 'Portfolio link' }] });

    expect(screen.getByRole('textbox', { name: /Portfolio link/ })).toHaveAttribute('type', 'url');
  });
});

describe('QuestionnairePreview pages', () => {
  it('shows each named section as a page with the step line the candidate sees', () => {
    render(QuestionnairePreview, {
      questions,
      sections: [
        { id: 'eligibility', name: 'Eligibility', questionIds: ['work_authorization'] },
        { id: 'about', name: 'About the role', questionIds: ['shifts', 'years', 'why'] },
      ],
    });

    const pages = [...document.querySelectorAll<HTMLElement>('[data-preview-page]')];
    expect(pages.map((page) => page.querySelector('h3')?.textContent)).toEqual(['Eligibility', 'About the role']);
    // Details, two pages and the review: four steps.
    expect(within(pages[1]!).getByText('Step 3 of 4')).toBeInTheDocument();
    expect(within(pages[0]!).getByText('0 of 5 answered')).toBeInTheDocument();
  });

  it("splits a questionnaire without sections at the organization's page size, and counts answers as they are tried", async () => {
    render(QuestionnairePreview, { questions, questionsPerPage: 2 });

    expect(document.querySelectorAll('[data-preview-page]')).toHaveLength(2);
    await fireEvent.click(screen.getByRole('radio', { name: 'Yes' }));
    expect(screen.getAllByText('1 of 5 answered').length).toBeGreaterThan(0);
  });

  it('pages a follow-up questionnaire without the details step', () => {
    render(QuestionnairePreview, { questions, followUp: true, questionsPerPage: 3 });

    expect(screen.getByText('Step 2 of 2')).toBeInTheDocument();
    expect(screen.queryByText('Your details')).toBeNull();
  });
});
