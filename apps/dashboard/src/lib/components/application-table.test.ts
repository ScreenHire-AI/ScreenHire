import { renderSnippet } from '@tanstack/svelte-table';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { candidateColumns } from '#lib/candidate-table.js';
import { integrityColumns } from '#lib/integrity-columns.js';
import { preferences } from '#lib/preferences.svelte.js';
import { applications } from '../../test-support/fixtures.js';
import { summary } from '../../test-support/integrity-fixtures.js';
import { stubViewport } from '../../test-support/viewport.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import ApplicationTable from './application-table.svelte';

const headerTexts = () => screen.getAllByRole('columnheader').map((header) => header.textContent?.trim());
const dataRows = () => screen.getAllByRole('row').filter((row) => within(row).queryAllByRole('cell').length > 1);
const rowOf = (name: string) => screen.getByRole('link', { name }).closest('tr')!;

afterEach(() => {
  preferences.setShortcutsEnabled(true);
  vi.unstubAllGlobals();
});

describe('candidate table', () => {
  it('shows a 48px row per application with its stage as a pill, source and date, newest first', () => {
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications } } });

    expect(headerTexts()).toEqual(['', 'Candidate', 'Stage', 'Source', 'Applied']);
    expect(dataRows().map((row) => within(row).getAllByRole('cell').slice(1).map((cell) => cell.textContent?.trim()))).toEqual([
      ['CM Casey Morgan', 'Applied', 'careers', 'Oct 20, 2026'],
      ['JL Jordan Lee', 'Screening', 'recruiter', 'Oct 19, 2026'],
      ['ST Sam Taylor', 'Applied', 'careers', 'Oct 17, 2026'],
    ]);
    expect(dataRows().every((row) => row.classList.contains('h-12'))).toBe(true);
    expect(within(rowOf('Jordan Lee')).getByText('Screening')).toHaveAttribute('data-slot', 'badge');
    expect(screen.getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute('href', `/applications/${applications[0]!.id}`);
  });

  it('adds the job column for the table across jobs', () => {
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications, showJob: true } } });

    expect(headerTexts()).toEqual(['', 'Candidate', 'Job', 'Stage', 'Source', 'Applied']);
    expect(within(rowOf('Casey Morgan')).getByRole('link', { name: 'Senior .NET Engineer' })).toBeInTheDocument();
  });

  it('puts extra columns, such as the integrity ones, between Stage and Source', () => {
    const level = createRawSnippet((name: () => string) => ({ render: () => `<span>Level for ${name()}</span>` }));
    const extraColumns = [
      candidateColumns.display({ id: 'authenticity', header: 'Authenticity', cell: ({ row }) => renderSnippet(level, row.original.candidateName) }),
    ];
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications, extraColumns } } });

    expect(headerTexts()).toEqual(['', 'Candidate', 'Stage', 'Authenticity', 'Source', 'Applied']);
    expect(within(rowOf('Jordan Lee')).getByText('Level for Jordan Lee')).toBeInTheDocument();
  });

  it('shows the integrity chips and sorts by level, highest first, with pending rows last either way', async () => {
    const rows = [
      { ...applications[0]!, integrity: summary({ aiAssistance: 'SomeIndicators', aiAssistanceReasons: ['CNT_TEMPLATED_ANSWER'] }) },
      { ...applications[1]!, integrity: null },
      { ...applications[2]!, integrity: summary({ authenticity: 'VerificationRecommended', authenticityReasons: ['A', 'B'] }) },
    ];
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications: rows, extraColumns: integrityColumns } } });
    const names = () => dataRows().map((row) => within(row).getAllByRole('link')[0]!.textContent);
    const header = () => screen.getByRole('columnheader', { name: /Authenticity/ });

    expect(headerTexts()).toEqual(['', 'Candidate', 'Stage', 'Authenticity', 'AI assistance', 'Source', 'Applied']);
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
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications } } });
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
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications } } });
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
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications } } });

    await fireEvent.click(screen.getByRole('checkbox', { name: 'Select all candidates' }));

    expect(screen.getAllByRole('checkbox', { name: /^Select (?!all)/ }).every((box) => (box as HTMLInputElement).checked)).toBe(true);
  });

  it('gives each checkbox a label as its hit area, 44px on touch (F4)', async () => {
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications } } });

    for (const box of screen.getAllByRole('checkbox')) {
      expect(box.parentElement?.tagName).toBe('LABEL');
      expect(box.parentElement).toHaveClass('size-6', 'pointer-coarse:size-11');
    }
    await fireEvent.click(screen.getAllByRole('checkbox', { name: /^Select (?!all)/ })[0]!.parentElement!);
    expect(screen.getAllByRole('checkbox', { name: /^Select (?!all)/ })[0]).toBeChecked();
  });

  it('keeps its sr-only labels inside the scroller, so a phone page does not scroll sideways (F1)', () => {
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications } } });

    expect(screen.getByRole('table').parentElement).toHaveClass('relative', 'overflow-auto');
  });

  it('keeps the checkbox and name columns stuck to the left while the table scrolls sideways (section 8)', () => {
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications, showJob: true, extraColumns: integrityColumns } } });
    const header = (name: string) => screen.getAllByRole('columnheader').find((cell) => cell.textContent?.trim() === name)!;

    for (const cell of [header(''), ...within(rowOf('Casey Morgan')).getAllByRole('cell').slice(0, 1)]) {
      expect(cell).toHaveClass('sticky', 'left-0');
    }
    expect(header('Candidate')).toHaveClass('sticky', 'left-10');
    expect(rowOf('Casey Morgan').querySelector('td:nth-child(2)')).toHaveClass('sticky', 'left-10', 'bg-card');
    expect(header('Job')).not.toHaveClass('sticky');
  });

  it('turns rows into two-line list items on a phone, status first and facts as pills (section 8)', () => {
    stubViewport(390);
    const rows = [
      { ...applications[0]!, integrity: summary({ authenticity: 'ReviewSuggested', authenticityReasons: ['A', 'B'] }) },
      { ...applications[1]!, integrity: summary({}) },
    ];
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications: rows, showJob: true, label: 'Candidates' } } });

    expect(screen.queryByRole('table')).toBeNull();
    const items = within(screen.getByRole('list', { name: 'Candidates' })).getAllByRole('listitem');
    expect(items.map((item) => within(item).getAllByRole('link')[0]!.textContent)).toEqual(['Casey Morgan', 'Jordan Lee']);
    expect(within(items[0]!).getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute('href', `/applications/${applications[0]!.id}`);
    // Attention gets a filled badge and a count pill; a calm level stays quiet (T3, T4).
    const casey = items[0]!.querySelector('[data-integrity-chip="authenticity"]')!;
    expect(casey.querySelector('[data-slot="badge"]')).toHaveClass('bg-status-review-soft');
    expect(casey).toHaveTextContent('2');
    expect(items[1]!.querySelector('[data-integrity-chip="authenticity"]')).toHaveAttribute('data-quiet');
    expect(within(items[1]!).getByText('Screening')).toHaveAttribute('data-slot', 'badge');
    expect(within(items[0]!).getByText('Oct 20, 2026')).toBeInTheDocument();
  });

  it('ignores single-key shortcuts when they are turned off', async () => {
    preferences.setShortcutsEnabled(false);
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications } } });
    const first = screen.getByRole('link', { name: 'Casey Morgan' });
    first.focus();

    await fireEvent.keyDown(first, { key: 'j' });

    expect(first).toHaveFocus();
  });

  it('has no always-on bar of shortcut hints or footer; the ? dialog lists the keys (K1)', () => {
    const { container } = render(WithTooltips, { props: { component: ApplicationTable, props: { applications } } });

    expect(container.querySelector('kbd')).toBeNull();
    expect(screen.queryByText(/\d+ candidates?\b/)).toBeNull();
  });

  it('says why it is empty below the headers, outside the scroller, with Clear filters while filtered (E1, F1)', async () => {
    const onclearfilters = vi.fn();
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications: [], emptyText: 'No candidates in this stage.', onclearfilters } } });

    const message = screen.getByText('No candidates in this stage.');
    expect(screen.getByRole('table').parentElement).not.toContainElement(message);
    expect(screen.queryAllByRole('cell')).toHaveLength(0);
    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(onclearfilters).toHaveBeenCalledOnce();
  });

  it('renders only the rows in view for a long list', () => {
    const many = Array.from({ length: 500 }, (_, index) => ({
      ...applications[0]!,
      id: `01a157d4-0000-7000-8000-${index.toString().padStart(12, '0')}`,
      candidateName: `Candidate ${index}`,
    }));
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications: many } } });

    expect(dataRows().length).toBeGreaterThan(10);
    expect(dataRows().length).toBeLessThan(60);
    expect(screen.getByRole('table')).toHaveAttribute('aria-rowcount', '501');
  });
});

describe('candidate table on a phone', () => {
  it('shows each application as a list item: authenticity first, the name as the link, facts as pills', () => {
    stubViewport(360);
    const rows = [{ ...applications[0]!, integrity: summary({ authenticity: 'ReviewSuggested' }) }, applications[1]!];
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications: rows, label: 'Candidates for Senior .NET Engineer', showJob: true } } });

    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    const items = within(screen.getByRole('list', { name: 'Candidates for Senior .NET Engineer' })).getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(within(items[0]!).getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute('href', `/applications/${applications[0]!.id}`);
    expect(items[0]!.querySelector('[data-integrity-chip="authenticity"]')).toHaveAttribute('data-level', 'Review suggested');
    expect(items[0]).toHaveTextContent('Applied');
    expect(items[0]).toHaveTextContent('Senior .NET Engineer');
    // The canvas's phone item has no source pill; the source is a column on wider screens and on the application page.
    expect(items[0]).toHaveTextContent('Oct 20, 2026');
    expect(items[1]!.querySelector('[data-integrity-chip="authenticity"]')).toHaveAttribute('data-level', 'pending');
  });

  it('says why when there is nothing to show', () => {
    stubViewport(360);
    render(WithTooltips, { props: { component: ApplicationTable, props: { applications: [], emptyText: 'No candidates yet.' } } });

    expect(screen.getByText('No candidates yet.')).toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
  });
});
