// The shared question components (packages/ui) as the dashboard's live preview renders them: the same native controls,
// names and ids the careers apply page serves, here interactive, a step at a time as the candidate meets them.
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

// Each step is its own screen: Next and Back move between them, as on the careers page.
const next = () => fireEvent.click(screen.getByRole('button', { name: 'Next' }));
const labels = () => [...document.querySelectorAll('label[for], legend')].map((element) => element.textContent?.replace(/\s+/g, ' ').trim());

describe('QuestionnairePreview', () => {
  it('asks for the candidate details and the resume first, then the questions on the next step', async () => {
    render(QuestionnairePreview, { questions });

    expect(screen.getByRole('heading', { name: 'Your details' })).toBeInTheDocument();
    expect(labels()).toEqual(['First name', 'Last name (optional)', 'Email', 'Phone (optional)', 'Resume or CV (optional)']);
    expect(screen.queryByRole('button', { name: 'Back' })).toBeNull();

    await next();

    expect(labels()).toEqual(['Can you work in the UK?', 'Which shifts suit you? (optional)', 'Years in a warehouse (optional)', 'Why this role? (optional)']);
  });

  it('renders yes/no as radios and multiple choice as checkboxes, named as the careers form posts them', async () => {
    render(QuestionnairePreview, { questions });
    expect(screen.getByLabelText(/Resume or CV/)).toHaveAttribute('type', 'file');
    await next();

    const authorization = screen.getByRole('group', { name: 'Can you work in the UK?' });
    expect(authorization).toHaveAttribute('id', 'field-q-work_authorization');
    expect(within(authorization).getAllByRole('radio').map((radio) => radio.getAttribute('name'))).toEqual(['q:work_authorization', 'q:work_authorization']);
    expect(within(screen.getByRole('group', { name: /Which shifts/ })).getAllByRole('checkbox')).toHaveLength(2);
  });

  it('gives hints and limits the native way', async () => {
    render(QuestionnairePreview, { questions });
    expect(screen.getByText('PDF or DOCX, up to 10 MB.')).toBeInTheDocument();
    await next();

    expect(screen.getByText('A number from 0 up to 50.')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: /Years in a warehouse/ })).toHaveAttribute('inputmode', 'decimal');
    const why = screen.getByRole('textbox', { name: /Why this role/ });
    expect(why.tagName).toBe('TEXTAREA');
    expect(why).toHaveAttribute('maxlength', '1000');
    expect(why).toHaveAccessibleDescription('A few sentences.');
  });

  it('lets the recruiter try answers, and keeps them going back and forth', async () => {
    render(QuestionnairePreview, { questions });
    await next();

    const yes = screen.getByRole('radio', { name: 'Yes' });
    const no = screen.getByRole('radio', { name: 'No' });
    await fireEvent.click(yes);
    await fireEvent.click(no);
    expect(no).toBeChecked();
    expect(yes).not.toBeChecked();
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Days' }));
    await fireEvent.click(screen.getByRole('checkbox', { name: 'Nights' }));

    await fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('heading', { name: 'Your details' })).toHaveFocus();
    await next();
    expect(screen.getByRole('radio', { name: 'No' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Days' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Nights' })).toBeChecked();
  });

  it('follows the questionnaire as it is edited', async () => {
    const view = render(QuestionnairePreview, { questions });
    await next();

    await view.rerender({ questions: [...questions, { id: 'site', type: 'Url', label: 'Portfolio link' }] });

    expect(screen.getByRole('textbox', { name: /Portfolio link/ })).toHaveAttribute('type', 'url');
  });

  it('ends on the review step, whose send button is off and says nothing is sent', async () => {
    render(QuestionnairePreview, { questions, aiUse: 'Allowed' });
    await next();
    await next();

    expect(screen.getByRole('heading', { name: 'Review and send' })).toHaveFocus();
    expect(screen.getByRole('group', { name: /Did you use AI tools for this application\? \(optional\)/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Next' })).toBeNull();
    const send = screen.getByRole('button', { name: 'Send application' });
    expect(send).toBeDisabled();
    expect(send).toHaveAccessibleDescription('Nothing is sent from a preview.');
  });
});

describe('QuestionnairePreview pages', () => {
  const twoSections = [
    { id: 'eligibility', name: 'Eligibility', questionIds: ['work_authorization'] },
    { id: 'about', name: 'About the role', questionIds: ['shifts', 'years', 'why'] },
  ];

  it('shows each named section as its own step with the step line the candidate sees', async () => {
    render(QuestionnairePreview, { questions, sections: twoSections });

    // Details, two pages and the review: four steps.
    expect(screen.getByText('Step 1 of 4')).toBeInTheDocument();
    await next();
    expect(screen.getByRole('heading', { name: 'Eligibility' })).toHaveFocus();
    expect(screen.getByText('0 of 5 answered')).toBeInTheDocument();
    await next();
    expect(screen.getByRole('heading', { name: 'About the role' })).toBeInTheDocument();
    expect(screen.getByText('Step 3 of 4')).toBeInTheDocument();
    expect(document.querySelectorAll('[data-preview-page]')).toHaveLength(1);
  });

  it("splits a questionnaire without sections at the organization's page size, and counts answers as they are tried", async () => {
    render(QuestionnairePreview, { questions, questionsPerPage: 2 });
    await next();

    expect(screen.getByText('Step 2 of 4')).toBeInTheDocument();
    await fireEvent.click(screen.getByRole('radio', { name: 'Yes' }));
    expect(screen.getByText('1 of 5 answered')).toBeInTheDocument();
  });

  it('pages a follow-up questionnaire without the details step, ending in Send answers', async () => {
    render(QuestionnairePreview, { questions, followUp: true, questionsPerPage: 3 });

    expect(screen.queryByText('Your details')).toBeNull();
    await next();
    expect(screen.getByText('Step 2 of 2')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send answers' })).toBeDisabled();
  });

  it('stays on a step that still exists when an edit removes pages', async () => {
    const view = render(QuestionnairePreview, { questions, sections: twoSections });
    await next();
    await next();
    await next();
    expect(screen.getByText('Step 4 of 4')).toBeInTheDocument();

    await view.rerender({ questions, sections: null, questionsPerPage: 8 });

    expect(screen.getByText('Step 3 of 3')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Review and send' })).toBeInTheDocument();
  });
});
