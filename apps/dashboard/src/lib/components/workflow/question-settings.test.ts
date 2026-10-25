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
});
