import type { ScorecardEntry, TeamMember } from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import WithTooltips from '../../../test-support/with-tooltips.svelte';
import ScorecardsTab from './scorecards-tab.svelte';

beforeEach(() => localStorage.clear());
afterEach(() => {
  cleanup();
  localStorage.clear();
});

const members = [
  { userId: 'u1', displayName: 'Avery Quinn', roleName: 'Owner' },
  { userId: 'u2', displayName: 'Robin Ashby', roleName: 'Interviewer' },
  { userId: 'u3', displayName: 'Jordan Ellis', roleName: 'Hiring manager' },
] as TeamMember[];

const card = (overrides: Partial<ScorecardEntry>): ScorecardEntry =>
  ({
    id: crypto.randomUUID(),
    stageId: 'interview',
    reviewerUserId: 'u2',
    answers: [{ questionId: 'q1', label: 'Problem solving', type: 'Rating5', rating: 5 }],
    rating: 4.5,
    ratings: [],
    recommendation: 'Yes',
    note: null,
    workflowVersion: 1,
    timelineEventId: 'e1',
    submittedAt: '2026-11-04T10:00:00Z',
    scorecardTemplateId: 'interview',
    scorecardName: 'Team interview',
    withheld: false,
    ...overrides,
  }) as ScorecardEntry;

const jordan = card({ id: 'jordan', reviewerUserId: 'u3', recommendation: 'StrongYes', rating: 4.8, submittedAt: '2026-11-06T10:00:00Z', note: 'Clear design.' });
const robin = card({ id: 'robin', recommendation: 'No', rating: 2, submittedAt: '2026-11-04T10:00:00Z' });
const avery = card({ id: 'avery', reviewerUserId: 'u1', recommendation: 'Yes', rating: 3.5, submittedAt: '2026-11-03T10:00:00Z' });

function show(props: Record<string, unknown> = {}) {
  return render(WithTooltips, {
    props: {
      component: ScorecardsTab,
      props: { scorecards: [robin, jordan, avery], stageName: () => 'Interview', members, currentUserId: 'u1', announcement: '', onretry: () => {}, ...props },
    },
  });
}

const names = () =>
  within(screen.getByRole('table', { name: 'Scorecards' }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('cell')[0]!.textContent?.trim());

describe('the Scorecards roster', () => {
  it('shows one row per reviewer, newest first, with the recommendation in words and the rating as a number', () => {
    show();

    expect(names()).toEqual(['Jordan Ellis', 'Robin Ashby', 'You']);
    const [first] = within(screen.getByRole('table', { name: 'Scorecards' })).getAllByRole('row').slice(1);
    expect(first).toHaveTextContent('Strong yes');
    expect(first).toHaveTextContent('4.8');
    // Words in ink, semibold, never a status colour (it must not read like a verification result).
    expect(first!.querySelector('[data-status]')).toBeNull();
    expect(within(first!).getByText('Strong yes')).toHaveClass('font-semibold', 'text-foreground');
    expect(within(first!).queryByRole('img')).not.toBeInTheDocument();
  });

  it('sorts by every header, and a second click reverses it', async () => {
    show();
    const table = screen.getByRole('table', { name: 'Scorecards' });
    const headers = within(table).getAllByRole('columnheader');
    expect(headers.map((header) => header.textContent?.trim())).toEqual(['Reviewer', 'Stage', 'Recommendation', 'Rating', 'Submitted']);
    for (const header of headers) expect(within(header).getByRole('button')).toBeInTheDocument();

    await fireEvent.click(within(table).getByRole('button', { name: 'Reviewer' }));
    expect(names()).toEqual(['Jordan Ellis', 'Robin Ashby', 'You']);
    await fireEvent.click(within(table).getByRole('button', { name: 'Reviewer' }));
    expect(names()).toEqual(['You', 'Robin Ashby', 'Jordan Ellis']);

    // The recommendation sorts by how favourable it is, not by its letters: the most favourable first, then reversed.
    await fireEvent.click(within(table).getByRole('button', { name: 'Recommendation' }));
    expect(within(table).getByRole('columnheader', { name: /Recommendation/ })).toHaveAttribute('aria-sort', 'descending');
    expect(names()).toEqual(['Jordan Ellis', 'You', 'Robin Ashby']);
    await fireEvent.click(within(table).getByRole('button', { name: 'Recommendation' }));
    expect(names()).toEqual(['Robin Ashby', 'You', 'Jordan Ellis']);
  });

  it("opens a row in a side sheet titled with the scorecard, with the reviewer's role and the note, and steps in the table's order", async () => {
    show();
    await fireEvent.click(screen.getByRole('button', { name: 'Jordan Ellis' }));

    const sheet = await screen.findByRole('dialog', { name: 'Team interview scorecard' });
    expect(sheet).toHaveTextContent('Jordan Ellis, Hiring manager');
    expect(sheet).toHaveTextContent('"Clear design."');
    expect(within(sheet).getByRole('button', { name: /Previous/ })).toBeDisabled();

    await fireEvent.click(within(sheet).getByRole('button', { name: /Next/ }));
    await waitFor(() => expect(sheet).toHaveTextContent('Robin Ashby, Interviewer'));
    await fireEvent.click(within(sheet).getByRole('button', { name: /Next/ }));
    await waitFor(() => expect(sheet).toHaveTextContent('You, Owner'));
    expect(within(sheet).getByRole('button', { name: /Next/ })).toBeDisabled();
    await fireEvent.click(within(sheet).getByRole('button', { name: /Previous/ }));
    await waitFor(() => expect(sheet).toHaveTextContent('Robin Ashby, Interviewer'));
  });

  it('draws a withheld scorecard with its reviewer and date only, and steps over it', async () => {
    const hidden = card({ id: 'hidden', reviewerUserId: 'u3', withheld: true, recommendation: null, rating: null, answers: [], submittedAt: '2026-11-06T10:00:00Z' });
    show({ scorecards: [hidden, robin], withheld: true, currentUserId: 'u4' });

    const table = screen.getByRole('table', { name: 'Scorecards' });
    const [hiddenRow, robinRow] = within(table).getAllByRole('row').slice(1);
    expect(hiddenRow).toHaveTextContent('Jordan Ellis');
    expect(within(hiddenRow!).queryByRole('button')).not.toBeInTheDocument();
    expect(hiddenRow).not.toHaveTextContent('Strong');
    expect(within(hiddenRow!).getAllByText('Hidden until you submit yours')).toHaveLength(2);
    expect(within(robinRow!).getByRole('button', { name: 'Robin Ashby' })).toBeInTheDocument();

    await fireEvent.click(within(robinRow!).getByRole('button', { name: 'Robin Ashby' }));
    const sheet = await screen.findByRole('dialog');
    // The only scorecard that can be read: nothing to step to, so no Previous or Next.
    expect(within(sheet).queryByRole('button', { name: /Previous|Next/ })).not.toBeInTheDocument();
  });

  it('lists scorecards asked for that are not in yet under their own heading, without a control', () => {
    show({
      scorecards: [robin],
      waiting: [{ taskId: 't1', assigneeUserId: 'u3', scorecard: 'Portfolio review scorecard', askedAt: '2026-11-05T10:00:00Z', askedByUserId: 'u1' }],
    });

    expect(screen.getByText('Asked for, not in yet')).toBeInTheDocument();
    const row = screen.getByText('Jordan Ellis').closest('tr')!;
    expect(row).toHaveTextContent('Waiting');
    expect(row).toHaveTextContent('for the Portfolio review scorecard');
    expect(row).toHaveTextContent('by you');
    expect(within(row).queryByRole('button')).not.toBeInTheDocument();
  });
});

describe('the Scorecards tab', () => {
  it('offers Fill in and Ask for one in the header while the reader may', () => {
    show({ onfill: vi.fn(), onask: vi.fn() });

    expect(screen.getByRole('button', { name: 'Fill in' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ask for one' })).toBeInTheDocument();
    expect(screen.queryByText('Submit yours to see the others.')).not.toBeInTheDocument();
  });

  it('says in one line to submit yours, with the one Fill in your scorecard, while a scorecard is still to give', async () => {
    const onfill = vi.fn();
    show({ withheld: true, onfill, onask: vi.fn() });

    expect(screen.getByText('Submit yours to see the others.')).toBeInTheDocument();
    // The line carries the one Fill in, so the header does not offer another.
    expect(screen.getAllByRole('button', { name: /^Fill in/ })).toHaveLength(1);
    await fireEvent.click(screen.getByRole('button', { name: 'Fill in your scorecard' }));
    expect(onfill).toHaveBeenCalledOnce();
  });

  it("fills Fill in your scorecard only for a reader who cannot move candidates (the page's one filled button)", () => {
    show({ withheld: true, onfill: vi.fn(), primaryFill: false });
    expect(screen.getByRole('button', { name: 'Fill in your scorecard' }).className).not.toMatch(/bg-primary(?!\/)/);
    cleanup();

    show({ withheld: true, onfill: vi.fn(), primaryFill: true });
    expect(screen.getByRole('button', { name: 'Fill in your scorecard' }).className).toMatch(/bg-primary/);
  });

  it('says there are none, but not while the reader has one to give', () => {
    show({ scorecards: [] });
    expect(screen.getByText('No scorecards yet.')).toBeInTheDocument();
    cleanup();

    show({ scorecards: [], withheld: true });
    expect(screen.queryByText('No scorecards yet.')).not.toBeInTheDocument();
  });

  it('draws list items with a Sort chip on a phone', async () => {
    show({ phone: true });

    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Sort/ })).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Scorecards' })).getAllByRole('listitem')).toHaveLength(3);
    await fireEvent.click(screen.getByRole('button', { name: 'Jordan Ellis' }));
    expect(await screen.findByRole('dialog', { name: 'Team interview scorecard' })).toBeInTheDocument();
  });
});
