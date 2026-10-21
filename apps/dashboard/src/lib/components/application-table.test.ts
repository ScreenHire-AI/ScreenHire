import { renderSnippet } from '@tanstack/svelte-table';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { afterEach, describe, expect, it } from 'vitest';
import { candidateColumns } from '#lib/candidate-table.js';
import { integrityColumns } from '#lib/integrity-columns.js';
import { preferences } from '#lib/preferences.svelte.js';
import { applications } from '../../test-support/fixtures.js';
import { summary } from '../../test-support/integrity-fixtures.js';
import ApplicationTable from './application-table.svelte';

const headerTexts = () => screen.getAllByRole('columnheader').map((header) => header.textContent?.trim());
const dataRows = () => screen.getAllByRole('row').filter((row) => within(row).queryAllByRole('cell').length > 1);
const rowOf = (name: string) => screen.getByRole('link', { name }).closest('tr')!;

afterEach(() => {
  preferences.setShortcutsEnabled(true);
});

describe('candidate table', () => {
  it('shows a 40 px row per application with its stage, source and date, newest first', () => {
    render(ApplicationTable, { props: { applications } });

    expect(headerTexts()).toEqual(['', 'Candidate', 'Stage', 'Source', 'Applied']);
    expect(dataRows().map((row) => within(row).getAllByRole('cell').slice(1).map((cell) => cell.textContent?.trim()))).toEqual([
      ['CM Casey Morgan', 'Applied', 'careers', 'Oct 20, 2026'],
      ['JL Jordan Lee', 'Screening', 'recruiter', 'Oct 19, 2026'],
      ['ST Sam Taylor', 'Applied', 'careers', 'Oct 17, 2026'],
    ]);
    expect(dataRows().every((row) => row.classList.contains('h-10'))).toBe(true);
    expect(screen.getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute('href', `/applications/${applications[0]!.id}`);
  });

  it('adds the job column for the table across jobs', () => {
    render(ApplicationTable, { props: { applications, showJob: true } });

    expect(headerTexts()).toEqual(['', 'Candidate', 'Job', 'Stage', 'Source', 'Applied']);
    expect(within(rowOf('Casey Morgan')).getByRole('link', { name: 'Senior .NET Engineer' })).toBeInTheDocument();
  });

  it('puts extra columns, such as the integrity ones, between Stage and Source', () => {
    const level = createRawSnippet((name: () => string) => ({ render: () => `<span>Level for ${name()}</span>` }));
    const extraColumns = [
      candidateColumns.display({ id: 'authenticity', header: 'Authenticity', cell: ({ row }) => renderSnippet(level, row.original.candidateName) }),
    ];
    render(ApplicationTable, { props: { applications, extraColumns } });

    expect(headerTexts()).toEqual(['', 'Candidate', 'Stage', 'Authenticity', 'Source', 'Applied']);
    expect(within(rowOf('Jordan Lee')).getByText('Level for Jordan Lee')).toBeInTheDocument();
  });

  it('shows the integrity chips and sorts by level, highest first, with pending rows last either way', async () => {
    const rows = [
      { ...applications[0]!, integrity: summary({ aiAssistance: 'SomeIndicators', aiAssistanceReasons: ['CNT_TEMPLATED_ANSWER'] }) },
      { ...applications[1]!, integrity: null },
      { ...applications[2]!, integrity: summary({ authenticity: 'VerificationRecommended', authenticityReasons: ['A', 'B'] }) },
    ];
    render(ApplicationTable, { props: { applications: rows, extraColumns: integrityColumns } });
    const names = () => dataRows().map((row) => within(row).getAllByRole('link')[0]!.textContent);
    const header = () => screen.getByRole('columnheader', { name: /Authenticity/ });

    expect(headerTexts()).toEqual(['', 'Candidate', 'Stage', 'Authenticity', 'AI assist', 'Source', 'Applied']);
    expect(within(rowOf('Sam Taylor')).getByText('Authenticity: Verification recommended, 2 reasons')).toBeInTheDocument();
    expect(within(rowOf('Casey Morgan')).getByText('AI assistance: Some indicators, 1 reason')).toBeInTheDocument();
    expect(within(rowOf('Jordan Lee')).getByText('Authenticity: Checks pending')).toBeInTheDocument();
    expect(header()).not.toHaveAttribute('aria-sort');

    await fireEvent.click(within(header()).getByRole('button', { name: 'Authenticity' }));
    expect(names()).toEqual(['Sam Taylor', 'Casey Morgan', 'Jordan Lee']);
    expect(header()).toHaveAttribute('aria-sort', 'descending');

    await fireEvent.click(within(header()).getByRole('button', { name: 'Authenticity' }));
    expect(names()).toEqual(['Casey Morgan', 'Sam Taylor', 'Jordan Lee']);
    expect(header()).toHaveAttribute('aria-sort', 'ascending');

    await fireEvent.click(within(header()).getByRole('button', { name: 'Authenticity' }));
    expect(names()).toEqual(['Casey Morgan', 'Jordan Lee', 'Sam Taylor']);
    expect(header()).not.toHaveAttribute('aria-sort');
  });

  it('moves the cursor with J and K, focusing and tinting the row', async () => {
    render(ApplicationTable, { props: { applications } });
    const first = screen.getByRole('link', { name: 'Casey Morgan' });
    first.focus();
    await fireEvent.focusIn(first);
    expect(rowOf('Casey Morgan')).toHaveClass('bg-row-selected');

    await fireEvent.keyDown(first, { key: 'j' });
    await waitFor(() => expect(screen.getByRole('link', { name: 'Jordan Lee' })).toHaveFocus());
    expect(rowOf('Jordan Lee')).toHaveClass('bg-row-selected');
    expect(rowOf('Casey Morgan')).not.toHaveClass('bg-row-selected');

    await fireEvent.keyDown(document.activeElement!, { key: 'k' });
    await waitFor(() => expect(first).toHaveFocus());
  });

  it('selects the cursor row with X, and its checkbox shows it', async () => {
    render(ApplicationTable, { props: { applications } });
    const link = screen.getByRole('link', { name: 'Jordan Lee' });
    link.focus();
    await fireEvent.focusIn(link);

    await fireEvent.keyDown(link, { key: 'x' });

    expect(screen.getByRole('checkbox', { name: 'Select Jordan Lee' })).toBeChecked();
    expect(rowOf('Jordan Lee')).toHaveClass('bg-row-selected');

    await fireEvent.keyDown(link, { key: 'x' });
    expect(screen.getByRole('checkbox', { name: 'Select Jordan Lee' })).not.toBeChecked();
  });

  it('selects every row from the header checkbox', async () => {
    render(ApplicationTable, { props: { applications } });

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select all candidates' }));

    expect(screen.getAllByRole('checkbox', { name: /^Select (?!all)/ }).every((box) => (box as HTMLInputElement).checked)).toBe(true);
  });

  it('ignores single-key shortcuts when they are turned off', async () => {
    preferences.setShortcutsEnabled(false);
    render(ApplicationTable, { props: { applications } });
    const first = screen.getByRole('link', { name: 'Casey Morgan' });
    first.focus();

    await fireEvent.keyDown(first, { key: 'j' });

    expect(first).toHaveFocus();
  });

  it('has no always-on bar of shortcut hints or footer; the ? dialog lists the keys (K1)', () => {
    const { container } = render(ApplicationTable, { props: { applications } });

    expect(container.querySelector('kbd')).toBeNull();
    expect(screen.queryByText(/\d+ candidates?\b/)).toBeNull();
  });

  it('says so when there is nothing to show', () => {
    render(ApplicationTable, { props: { applications: [], emptyText: 'No candidates in this stage.' } });

    expect(screen.getByRole('cell')).toHaveTextContent('No candidates in this stage.');
  });

  it('renders only the rows in view for a long list', () => {
    const many = Array.from({ length: 500 }, (_, index) => ({
      ...applications[0]!,
      id: `01a157d4-0000-7000-8000-${index.toString().padStart(12, '0')}`,
      candidateName: `Candidate ${index}`,
    }));
    render(ApplicationTable, { props: { applications: many } });

    expect(dataRows().length).toBeGreaterThan(10);
    expect(dataRows().length).toBeLessThan(60);
    expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '501');
  });
});
