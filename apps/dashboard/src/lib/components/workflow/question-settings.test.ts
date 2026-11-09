import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import QuestionSettings from './question-settings.svelte';

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

  it('says a knockout does nothing yet, and where to add a step, until an automation reads its answer (A11)', () => {
    const question = { id: 'licence', type: 'YesNo' as const, label: 'Do you hold a licence?', knockout: { disqualifyingAnswer: false, reason: null } };
    const { rerender } = render(QuestionSettings, { props: { question, unhandled: true } });

    expect(screen.getByText(/Nothing happens to this answer yet/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Add a step in Automations' })).toHaveAttribute('href', '?tab=automations');

    rerender({ question, unhandled: false });
    expect(screen.queryByText(/Nothing happens to this answer yet/)).toBeNull();
  });

  it('says nothing about a step for a question that is not a knockout', () => {
    const question = { id: 'licence', type: 'YesNo' as const, label: 'Do you hold a licence?' };
    render(QuestionSettings, { props: { question, unhandled: true } });

    expect(screen.queryByText(/Nothing happens to this answer yet/)).toBeNull();
  });

  it('draws nothing for a choice question, so the open question keeps even gaps', () => {
    const question = { id: 'years', type: 'SingleChoice' as const, label: 'Years in support', options: [] };
    const { container } = render(QuestionSettings, { props: { question } });

    expect(container.querySelector('div')).toBeNull();
  });
});
